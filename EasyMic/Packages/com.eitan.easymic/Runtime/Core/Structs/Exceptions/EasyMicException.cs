using System;
namespace Eitan.EasyMic.Runtime.Exceptions
{
    public class EasyMicException : Exception
    {
        public override string Message => $"[EasyMic]: {base.Message}";
        public EasyMicException(string message) : base(message)
        {

        }

    }
}