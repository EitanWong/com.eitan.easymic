# EasyMic APM 本地授权架构草案

本文是面向开发者客户端侧的本地授权架构草案。目标不是替代当前 native licensing，而是在现有实现之上，补上一层适合 Unity 客户端分发的“项目绑定 + native 参与派生 + license 验签”模块，并保持与编辑器 UI、APM runtime、native 授权核心低耦合。

## 现状结论

当前两侧已经具备的能力：

- Unity 侧：
  - 已有 `IEasyMicApmLicenseTokenProvider`
  - 已有 Editor 侧 token 导入、加密 payload 落盘、provider 生成
  - 已有 `StreamingAssets` 受保护 payload 机制
- native 侧：
  - 已有独立 `src/license` 模块
  - 已有 token 解析与验签
  - 已有 runtime identity collector
  - 已有 `device_locked` / `app_locked` 模型
  - 已有 `license_tool` 与 embedded public keys

因此，升级方向不应是把更多授权逻辑塞回 Unity Editor，而是新增一层独立的本地授权桥接模块：

- Unity 负责：
  - 读取本地 envelope
  - 收集项目绑定输入
  - 调用 native bridge 获取派生材料
  - 把最终 token 提供给 APM runtime
- native 负责：
  - 提供 runtime identity
  - 提供派生材料或派生函数
  - 验签与授权校验

## 目标

新模块需要满足：

- 不在 C# 中保存完整解密 key
- 不把 Unity 项目绑定逻辑硬编码进 APM runtime
- 不修改现有 native license verify 主流程的单一职责
- 可以作为单独模块存在，被 provider、启动器、编辑器工具按需接入

## 推荐模块边界

### Unity 侧独立模块

命名建议：

- `Eitan.EasyMic.Apm.Licensing`

职责：

- 读取 `license envelope`
- 收集 `project fingerprint`
- 调用 native bridge 获取 `derivation part`
- 解包出 vendor token
- 通过 `IEasyMicApmLicenseTokenProvider` 输出 token

明确不负责：

- 不直接修改 APM native 授权状态
- 不做 feature gating
- 不做业务 UI

### Native 侧独立模块

直接复用现有 `src/license`

新增职责：

- 提供 Unity 客户端本地授权桥接 C ABI
- 根据 `project fingerprint + payload nonce + native secret part` 产生派生材料
- 对 envelope 中签名进行验签
- 返回受控的状态码与调试信息

明确不负责：

- 不依赖 UnityEditor
- 不读取 Unity 场景/资源
- 不直接持有 Unity 工程路径

## 核心数据结构

### 1. License Envelope

本地不再直接存 token 明文，而是存 envelope：

- `version`
- `kid`
- `product_id`
- `payload_nonce`
- `binding_mode`
- `binding_hash`
- `ciphertext`
- `signature`

说明：

- `signature` 用 vendor 私钥签发
- `ciphertext` 里装的是实际 APM vendor token
- `binding_hash` 不是完整 fingerprint，而是标准化后的 hash

### 2. Project Fingerprint

建议由多个稳定字段组合后标准化：

- `applicationIdentifier`
- `cloudProjectId`
- `customProjectSecret`
- 可选：`packageName`

注意：

- 不建议直接绑定极易变化的字段
- `customProjectSecret` 应由 Unity 项目初始化后生成，并单独保存

推荐标准化流程：

1. 采集字段
2. 归一化大小写与空白
3. 拼接为 canonical string
4. 计算 `SHA-256`

### 3. Native Derivation Part

native 不直接返回最终 key，建议只返回一段派生材料：

- 输入：
  - `projectFingerprintHash`
  - `payloadNonce`
  - 可选：`productId`
- 输出：
  - `derivationPart[32]`

最终解密 key 由 Unity 侧使用 HKDF 或等价 KDF 合成：

- `finalKey = HKDF(projectFingerprintHash || derivationPart || payloadNonce)`

这样可以保证：

- C# 没有完整 native secret
- native 也没有 Unity 全部项目绑定输入的最终上下文

## 推荐流程

### 启动时

1. Unity 读取 `license envelope`
2. Unity 构建 `project fingerprint hash`
3. Unity 调 native bridge：
   - 验签 envelope
   - 获取 `derivation part`
4. Unity 本地派生最终解密 key
5. Unity 解出 vendor token
6. 通过 `IEasyMicApmLicenseTokenProvider.TryGetLicenseToken` 输出 token
7. APM runtime 仍按现有统一流程调用 provider 并授权

### 编辑器导入时

1. 导入 token 文件
2. 用 vendor 公钥/工具链生成 envelope
3. 写入 `StreamingAssets`
4. 生成 provider，仅引用本地 bridge 模块

## 和现有 native 代码的关系

native 现有代码里已经具备以下可直接复用能力：

- `easymic_apm_license_authorize*`
- `easymic_apm_license_validate_runtime_identity`
- `easymic_apm_runtime_identity_collect`
- embedded public keys
- token 签名验签

因此新桥接接口建议是“补接口”，而不是“重写授权核心”。

建议新增一组独立 C ABI，例如：

```c
EasyMicApmStatus easymic_apm_local_license_verify_envelope(
    const void* envelope_bytes,
    int32_t envelope_len);

EasyMicApmStatus easymic_apm_local_license_get_derivation_part(
    const uint8_t project_fingerprint_hash[32],
    const uint8_t payload_nonce[32],
    uint8_t out_part[32]);
```

它们应该位于单独的 bridge 模块中，而不是塞进主 APM 处理接口。

## 安全收益

升级后，相比当前 `ProtectedLicenseKey` 常量方案，收益是：

- C# 不再持有完整静态 key
- payload 被项目绑定
- native 参与 key 派生
- envelope 被 vendor 签名保护，无法随意伪造
- 运行时 token 输出仍走现有 provider 机制，不破坏当前授权主链路

## 安全边界声明

这是更安全的本地授权，不是绝对安全。

仍然成立的现实约束：

- token 最终要在客户端内存中使用
- native 代码仍可能被逆向
- 纯离线本地方案只能提高破解成本，不能提供服务端级别的强保证

## 推荐落地顺序

1. 在 native `src/license` 下新增 local license bridge C ABI
2. 定义 envelope 格式与签名字段
3. 在 Unity 新增独立 `Eitan.EasyMic.Apm.Licensing` 模块
4. 用新模块替换当前 `ProtectedLicenseKey` 直接解密模板
5. 保持 `IEasyMicApmLicenseTokenProvider` 作为最终 token 输出口

## 不建议的做法

- 不建议继续把完整 `ProtectedLicenseKey` 生成进 C# 常量
- 不建议让 Unity Editor 直接承担最终授权决策
- 不建议把项目绑定字段散落在多个业务脚本里
- 不建议让 APM runtime 知道 Unity 项目结构细节

