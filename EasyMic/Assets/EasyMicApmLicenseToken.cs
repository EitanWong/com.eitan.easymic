namespace Eitan.EasyMic.Generated.Licensing
{
    using Eitan.EasyMic.Runtime.Apm;
    using UnityEngine.Scripting;

    /// <summary>
    /// EasyMic APM license token provider.
    /// IMPORTANT: this default template stores token in plaintext.
    /// To reduce leakage risk, customize this file to fetch from server,
    /// split key material, or perform your own encryption/decryption.
    /// </summary>
    [Preserve]
    internal sealed class EasyMicApmProjectLicenseProvider : IEasyMicApmLicenseTokenProvider
    {
        public int Priority => 0;

        //Android
        // private const string PlainLicenseToken = @"eyJ2IjoxLCJraWQiOiJlYXN5bWljX2FwbV8yMDI2aDFfZWQyNTUxOV8wMSIsInBpZCI6ImNvbS5laXRhbi5lYXN5bWljLmFwbSIsImxtIjoiYXBwX2xvY2tlZCIsInRpZCI6ImVhc3ltaWNfYXBtX3Rlc3QiLCJhaWQiOiJjb20uZWl0YW4uZWFzeW1pYyIsInNpZCI6IiIsImZlYXQiOjE1LCJpYXQiOjE3NzIyOTQyMTAsImV4cCI6MCwibGljIjoiYzMwNjc2MzhjY2Y5NzcxYjkyZGNjYmViZGUxYzI4ODAifQ.DRA7ZjJrSsdmDFa13pYoaeUzUpplULDHEfxrNwtrYX-CW9ae0u2SyR8CYkEYWrvjyS6vDOifirFTCDt38_N3CA";


        //macOS
        private const string PlainLicenseToken = @"eyJ2IjoxLCJraWQiOiJlYXN5bWljX2FwbV8yMDI2aDFfZWQyNTUxOV8wMSIsInBpZCI6ImNvbS5laXRhbi5lYXN5bWljLmFwbSIsImxtIjoiZGV2aWNlX2xvY2tlZCIsIm1pZCI6IkY1cmNXckNQRWhWcnlqTHViV0lyYlp5bm1iRDRLR1dURDc0MFEyYjd5XzAiLCJmZWF0IjoxNSwiaWF0IjoxNzc0NjIxNjQyLCJleHAiOjAsImxpYyI6IjU2Yjc5YjM2ODY0YTZmMGIzNWNmZTY4ZjY0YWYxMjNjIn0.gnBZQDOr80_7_qhCLNy0PBuzbv481V0zwVn4KK_s-XHs4L0IkVAXAe6W-gDvLj0RQf7I4aSllMFGRNc0FrCMAg";


        public bool TryGetLicenseToken(out string token)
        {
            // If you encrypt token yourself, decrypt it here and return final plaintext token.
            token = PlainLicenseToken;
            return !string.IsNullOrWhiteSpace(token);
        }
    }
}
