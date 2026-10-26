using System.Runtime.InteropServices;

namespace Intonaco.Core
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);
    }
}
