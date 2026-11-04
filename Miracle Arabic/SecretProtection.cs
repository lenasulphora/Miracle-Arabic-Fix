using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Miracle
{
    internal static class SecretProtection
    {
        // Stops DPAPI from showing any UI (CRYPTPROTECT_UI_FORBIDDEN).
        private const int CryptprotectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob input, string? description, IntPtr optionalEntropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input, IntPtr description, IntPtr optionalEntropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        // Encrypts plain text and returns Base64; an empty value stays empty.
        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
            return Convert.ToBase64String(Transform(bytes, protect: true));
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return "";
            byte[] bytes;
            try { bytes = Convert.FromBase64String(protectedText); }
            catch (FormatException ex) { throw new CryptographicException("The saved DeepL key is not valid DPAPI data.", ex); }
            return Encoding.UTF8.GetString(Transform(bytes, protect: false));
        }

        private static byte[] Transform(byte[] inputBytes, bool protect)
        {
            IntPtr inputMemory = Marshal.AllocHGlobal(inputBytes.Length);
            DataBlob input = new DataBlob { Size = inputBytes.Length, Data = inputMemory };
            DataBlob output = default;
            try
            {
                Marshal.Copy(inputBytes, 0, inputMemory, inputBytes.Length);
                bool ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);
                if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
                byte[] result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (inputMemory != IntPtr.Zero)
                {
                    for (int i = 0; i < inputBytes.Length; i++) Marshal.WriteByte(inputMemory, i, 0);
                    Marshal.FreeHGlobal(inputMemory);
                }
                if (output.Data != IntPtr.Zero)
                {
                    for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
                CryptographicOperations.ZeroMemory(inputBytes);
            }
        }
    }
}
