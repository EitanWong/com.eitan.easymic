using UnityEngine;
using System;
using UnityEngine.UI;
using System.Linq;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Apm;
namespace Eitan.EasyMic.Apm.Samples
{

    public class EasyMicAudioProcessingExample : MonoBehaviour
    {

        #region UIComponent
        [Header("Recording Option")]
        [SerializeField] private Dropdown _selectDeviceDropdown;
        [SerializeField] private Dropdown _sampleRateDropdown;
        [SerializeField] private Dropdown _channelDropdown;

        [SerializeField] private Toggle _agcToggle;
        [SerializeField] private Toggle _ansToggle;
        [SerializeField] private Toggle _aecToggle;
        [SerializeField] private Toggle _loopbackToggle;
        [SerializeField] private Button _refreshButton;

        [Header("Recording Status")]
        [SerializeField] private RawImage _recordingStateImage;
        [SerializeField] private Text _recordingStateText;

        [SerializeField] private Button _recordButton;

        [Header("Recording Result")]
        [SerializeField] private RectTransform _resultPanel;
        [SerializeField] private Text _audioNameText;
        [SerializeField] private Button _playOrStopButton;
        [SerializeField] private Button _saveButton;
        #endregion

        private RecordingHandle _handle;
        private AudioCapturer _capture;

        private LoopbackPlayer _loopbackPlayer;
        private int _maxCaptureDuration = 30;

        // MODIFIED: 简化isRecording的判断逻辑
        private bool _isRecording => _handle.IsValid && EasyMicAPI.GetRecordingInfo(_handle).IsActive;
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private bool _wasPlaying = false;

        private WebRtcApmModifier _modifier;


        #region MonoBehaviour
        private void Start()
        {
            // 1. 初始化 AudioSource
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            // 2. 绑定UI事件监听
            _recordButton.onClick.AddListener(OnRecordButtonPressed);
            _refreshButton.onClick.AddListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.AddListener(PlayOrStopButtonClickHandle);
            _saveButton.onClick.AddListener(SaveButtonClickHandle);

            // 3. 设置UI的初始状态
            SetUIInteractivity(true);
            _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
            _recordingStateText.text = "Not Recording";
            _recordingStateImage.color = Color.gray;
            _resultPanel.gameObject.SetActive(false);
            _loopbackToggle.isOn = true;
            _agcToggle.isOn = false;
            _ansToggle.isOn = false;
            _aecToggle.isOn = false;


            OnRefreshButtonPressed();
        }

        private void Update()
        {
            if (_audioSource != null)
            {
                if (_wasPlaying && !_audioSource.isPlaying)
                {
                    _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
                }
                _wasPlaying = _audioSource.isPlaying;
            }


            // #region AEC Debug

            // if (_modifier!=null&& Input.GetKeyDown(KeyCode.Space))
            // {
            //     if (_modifier.IsDebugCapturing)
            //     {
            //         _modifier.StopDebugCapture();
            //     }
            //     else
            //     {

            //         _modifier.StartDebugCapture();
            //     }

            // }
            
            // #endregion
        }

        private void OnDestroy()
        {
            EasyMicAPI.StopAllRecordings();
            if (_saveButton) { _saveButton.onClick.RemoveAllListeners(); }
            if (_playOrStopButton) { _playOrStopButton.onClick.RemoveAllListeners(); }
            if (_recordButton)
            {
                _recordButton.onClick.RemoveAllListeners();
            }

            if (_refreshButton)
            {
                _refreshButton.onClick.RemoveAllListeners();
            }
            if (_agcToggle)
            {
                _aecToggle.onValueChanged.RemoveAllListeners();
            }
            if (_ansToggle)
            {
                _ansToggle.onValueChanged.RemoveAllListeners();
            }
            if (_aecToggle)
            {
                _aecToggle.onValueChanged.RemoveAllListeners();
            }
            if (_loopbackToggle)
            {
                _loopbackToggle.onValueChanged.RemoveAllListeners();
            }

        }
        #endregion

        #region UI Logic

        /// <summary>
        /// 统一控制UI控件的可交互状态。
        /// </summary>
        private void SetUIInteractivity(bool isInteractable)
        {
            _recordButton.interactable = isInteractable;
            _selectDeviceDropdown.interactable = isInteractable;
            _sampleRateDropdown.interactable = isInteractable;
            _channelDropdown.interactable = isInteractable;
            _agcToggle.interactable = isInteractable;
            _ansToggle.interactable = isInteractable;
            _aecToggle.interactable = isInteractable;
            // 刷新按钮总是可交互的，以便用户可以重试权限请求
            _refreshButton.interactable = true;
        }

        #endregion

        #region EventHandleMethod

        /// <summary>
        /// 刷新按钮现在也会触发权限检查。
        /// </summary>
        private void OnRefreshButtonPressed()
        {

            EasyMicAPI.Refresh();
            var devices = EasyMicAPI.Devices.ToList();
            _selectDeviceDropdown.ClearOptions();
            Channel defaultChannel = Channel.Mono;
            if (devices.Count > 0)
            {
                _selectDeviceDropdown.AddOptions(devices.Select(x => x.Name).ToList());
                int defaultIndex = devices.FindIndex(m => m.IsDefault);
                _selectDeviceDropdown.value = Mathf.Max(0, defaultIndex); // 确保索引不为-1
                defaultChannel = devices[defaultIndex].GetDeviceChannel();
            }
            else
            {
                Debug.LogWarning("No microphone devices found.");
            }

            _sampleRateDropdown.ClearOptions();
            _sampleRateDropdown.AddOptions(Enum.GetNames(typeof(SampleRate)).ToList());
            _sampleRateDropdown.value = _sampleRateDropdown.options.FindIndex(m => m.text == SampleRate.Hz16000.ToString());

            _channelDropdown.ClearOptions();
            _channelDropdown.AddOptions(Enum.GetNames(typeof(Channel)).ToList());

            _channelDropdown.value = _channelDropdown.options.FindIndex(m => m.text == defaultChannel.ToString());

        }

        /// <summary>
        /// 录制按钮的逻辑。由于UI状态控制，此方法只会在权限授予后被调用。
        /// </summary>
        private void OnRecordButtonPressed()
        {
            if (_isRecording) // 如果正在录音 -> 停止录音
            {
                _audioClip = _capture.GetCapturedAudioClip();
                EasyMicAPI.StopRecording(_handle);
                if (_modifier != null)
                { _modifier.Dispose(); }

                if (_capture != null)
                { _capture.Dispose(); }

                if (_loopbackPlayer != null)
                { _loopbackPlayer.Dispose(); }

                _handle = default; // 重置句柄

                // 更新UI到“停止”状态
                _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
                _recordingStateText.text = "Not Recording";
                _recordingStateImage.color = Color.gray;
                SetUIInteractivity(true);
                _resultPanel.gameObject.SetActive(true);
                _audioNameText.text = _audioClip.name;
            }
            else // 如果未在录音 -> 开始录音
            {
                if (_audioSource.isPlaying)
                {
                    _audioSource.Stop();
                }

                // 检查是否有可用设备

                if (EasyMicAPI.Devices.Length == 0)
                {
                    Debug.LogError("No microphone devices available to start recording.");
                    return;
                }

                var selectedName = _selectDeviceDropdown.options[_selectDeviceDropdown.value].text;
                var samplesRate = Enum.Parse<SampleRate>(_sampleRateDropdown.options[_sampleRateDropdown.value].text);
                var channel = Enum.Parse<Channel>(_channelDropdown.options[_channelDropdown.value].text);

                _handle = EasyMicAPI.StartRecording(selectedName, samplesRate, channel);
                if (!_handle.IsValid)
                {
                    Debug.LogError("Failed to start recording. Please check device compatibility.");
                    return;
                }

                if (_agcToggle.isOn || _ansToggle.isOn || _aecToggle.isOn)
                {

                    _modifier = new WebRtcApmModifier((int)channel, (int)samplesRate,aecEnabled: _aecToggle.isOn, nsEnabled: _ansToggle.isOn, agc1Enabled: _agcToggle.isOn, hpfEnabled: true,aecLatencyMs:-1);
                    _modifier.Enabled = true;
                    // _modifier.AutomaticGainControl.Agc1Enabled = _agcToggle.isOn;
                    // _modifier.NoiseSuppression.Enabled = _ansToggle.isOn;
                    // _modifier.EchoCancellation.Enabled = _aecToggle.isOn;
                    EasyMicAPI.AddProcessor(_handle, _modifier);
                }


                if (_capture == null)
                {
                    _capture = new AudioCapturer(_maxCaptureDuration);
                }
                
                EasyMicAPI.AddProcessor(_handle, _capture);


                
                if (_loopbackToggle.isOn)
                {

                    if (_loopbackPlayer == null)
                    {
                        _loopbackPlayer = new LoopbackPlayer(_audioSource);
                    }
                    // 添加Playback Loop 回放
                    EasyMicAPI.AddProcessor(_handle, _loopbackPlayer);
                }
                


                // 更新UI到“录制中”状态
                _recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
                _recordingStateText.text = "Recording...";
                _recordingStateImage.color = Color.red;
                SetUIInteractivity(false); // 录制时禁用选项更改
                _recordButton.interactable = true; // 但录制按钮本身要能按，以便停止
                _resultPanel.gameObject.SetActive(false);
            }
        }

        private void PlayOrStopButtonClickHandle()
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
                _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
            }
            else
            {
                _audioSource.clip = _audioClip;
                _audioSource.loop = false;
                _audioSource.Play();
                _playOrStopButton.GetComponentInChildren<Text>().text = "Stop";
            }
        }

        private void SaveButtonClickHandle()
        {
            _audioClip.Save(); // 假设您有一个AudioClip的扩展方法叫Save()
        }


        #endregion
    }

}