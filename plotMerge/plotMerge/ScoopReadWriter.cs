using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace plotMerge
{
    /// <summary>
    /// Summary description for ScoopReadWriter.
    /// </summary>
    public class ScoopReadWriter
	{
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFileValidData(SafeFileHandle hFile, long ValidDataLength);
		protected string _FileName;
        private long _lLength = -1;
		protected bool _bOpen = false;
        protected FileStream _fs;
        private long _lPosition = 0;
        FileOptions FileFlagNoBuffering = (FileOptions)0x20000000;

        //inline constructor
        public ScoopReadWriter(string stFileName)
		{
			_FileName = stFileName;
        }

        public void Close()
		{
			_bOpen = false;
            _fs.Close();
		}

        //Opens Source File for reading
        //directIO: true to use direct i/o
        //returns true on success
        public Boolean OpenR(bool directIO)
        {
            try
            {
                if (directIO)
                {
                    _fs = new FileStream(_FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileFlagNoBuffering);
                }
                else
                {
                    _fs = new FileStream(_FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: Error Opening File: "+ e.Message);
                if (_fs != null) _fs.Close();
                return false;
            }
            _lPosition = 0;
            _bOpen = true;
            return true;
        }

        //Opens Target File for writing
        //directIO: true to use direct i/o
        //returns true on success
        public Boolean OpenW(bool directIO)
        {
            try
            {
                //assert privileges
                if (!Privileges.HasAdminPrivileges)
                {
                    Console.Error.WriteLine("ERR: Error asserting required privilege. No elevated file creation possible.");
                    return false;
                }
                if (directIO)
                {
                    _fs = new FileStream(_FileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1048576, FileFlagNoBuffering);
                }
                else
                {
                    _fs = new FileStream(_FileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1048576, FileOptions.WriteThrough);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: Error Opening File: " + e.Message);
                if (_fs != null) _fs.Close();
                return false;
            }
            _lPosition = 0;
            _lLength = _fs.Length;
            _bOpen = true;
            return true;
        }

        //Reads a scoop from startNonce to limit 
        public Boolean ReadScoop(int scoop, long totalNonces, long startNonce, Scoop target, int limit)
		{
            _lPosition = scoop * (64 * totalNonces) + startNonce * 64;
            try
            {
                _fs.Seek(_lPosition, SeekOrigin.Begin);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: I/O Error - Seek to read failed: Scoop: " + scoop.ToString() + " " + e.Message);
                return false;
            }
            try
            {
                //limit reads to 1MB to avoid interrupts 64*16384
                for (int i = 0; i < limit * 64; i += (64 * 16384))
                    _fs.Read(target.byteArrayField, i, Math.Min(64 * 16384, limit * 64 - i));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: I/O Error - Read failed: Scoop: " + e.Message);
                return false;
            }
            _lPosition += limit * 64;
            return true;
        }

        //Writes a scoop from startNonce to limit 
        public Boolean WriteScoop(int scoop, long totalNonces, long startNonce, Scoop source, int limit)
        {
            _lPosition = scoop * (64 * totalNonces) + startNonce * 64;
            try
            {
                _fs.Seek(_lPosition, SeekOrigin.Begin);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: I/O Error - Seek to write failed: Scoop: " + scoop.ToString() + " " + e.Message);
                return false;
            }
            try
            {
                //interrupt avoider 1mb read 64*16384
                for (int i = 0; i < limit * 64; i += (64 * 16384))
                    _fs.Write(source.byteArrayField, i, Math.Min(64 * 16384, limit * 64 - i));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERR: I/O Error - Write failed: Scoop: " + e.Message);
                return false;
            }
            _lPosition += limit * 64;
            return true;
        }

        //returns true if files are openend
        public Boolean isOpen()
        {
            return _bOpen;
        }

        //preAllocate disk space and allow for fast file creation
        public Boolean PreAlloc(long totalNonces)
        {
            _fs.SetLength(totalNonces*(2<<17));
            bool test = SetFileValidData(_fs.SafeFileHandle, totalNonces * (2 << 17));
            if (!test) 
            {
                Console.Error.WriteLine("ERR: Quick File creation failed.");
                return false;
            }
            return true;
        }
	}


    public static class Privileges
    {
        private static int asserted = 0;
        private static bool hasBackupPrivileges = false;

        public static bool HasAdminPrivileges
        {
            get { return AssertPriveleges(); }
        }

        private static bool AssertPriveleges()
        {
            bool success = false;
            var wasAsserted = Interlocked.CompareExchange(ref asserted, 1, 0);
            if (wasAsserted == 0)
            {

                success = AssertPrivelege(NativeMethods.SE_MANAGE_VOLUME_NAME);

                hasBackupPrivileges = success;

            }
            return hasBackupPrivileges;
        }

        private static bool AssertPrivelege(string privelege)
        {
            IntPtr token;
            var tokenPrivileges = new NativeMethods.TOKEN_PRIVILEGES();
            tokenPrivileges.Privileges = new NativeMethods.LUID_AND_ATTRIBUTES[1];

            var success =
              NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TOKEN_ADJUST_PRIVILEGES, out token)
              &&
              NativeMethods.LookupPrivilegeValue(null, privelege, out tokenPrivileges.Privileges[0].Luid);

            try
            {
                if (success)
                {
                    tokenPrivileges.PrivilegeCount = 1;
                    tokenPrivileges.Privileges[0].Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;
                    success =
                      NativeMethods.AdjustTokenPrivileges(token, false, ref tokenPrivileges, Marshal.SizeOf(tokenPrivileges), IntPtr.Zero, IntPtr.Zero)
                      &&
                      (Marshal.GetLastWin32Error() == 0);
                }

                if (!success)
                {
                    Console.WriteLine("Could not assert privilege: " + privelege);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }

            return success;
        }
    }

    internal class NativeMethods
    {
        internal const int ERROR_HANDLE_EOF = 38;

        //--> Privilege constants....
        internal const UInt32 SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const string SE_MANAGE_VOLUME_NAME = "SeManageVolumePrivilege";

        //--> For starting a process in session 1 from session 0...
        internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);
        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)]bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, Int32 BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID
        {
            public UInt32 LowPart;
            public Int32 HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public UInt32 Attributes;
        }

        internal struct TOKEN_PRIVILEGES
        {
            public UInt32 PrivilegeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public LUID_AND_ATTRIBUTES[] Privileges;
        }
    }
}
