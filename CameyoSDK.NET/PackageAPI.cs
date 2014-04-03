using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace VirtPackageAPI
{
    public struct VirtFsNode
    {
        public String FileName;
        public VIRT_FILE_FLAGS FileFlags;
        public UInt64 CreationTime;
        public UInt64 LastAccessTime;
        public UInt64 LastWriteTime;
        public UInt64 ChangeTime;
        public UInt64 EndOfFile;
        public UInt32 FileAttributes;
        public Object ClientData;     // Arbitrary client data; you can use this however you like
    };

    [Flags]
    public enum VIRT_FILE_FLAGS
    {
        NO_FLAGS = 0x0,
        ISFILE = 0x0001,                  // File or directory?
        DELETED = 0x0002,                 // Deleted by virtual app (NOT_FOUND)
        DEPLOY_UPON_PRELOAD = 0x0008,     // Force file deploy (Disk mode)
        DISCONNECTED = 0x0010,            // Set when on-disk file is modified from DB
        PKG_FILE = 0x0020,                // File/dir is part of the original package (as opposed to files newly-added to sandbox during package use)
        DEPLOY_RAM_MODE = 0x0200,         // Force file deploy (RAM mode)
        ALL_FLAGS = ISFILE | DELETED | DEPLOY_UPON_PRELOAD | DISCONNECTED | PKG_FILE | DEPLOY_RAM_MODE
    }

    [Flags]
    public enum VIRT_PROCESS_FLAGS
    {
        VINTEGRATE_PROCESS_ONLY = 1,
        VINTEGRATE_RECURSIVE = 2
    }

    public class VirtPackage
    {
        public enum APIRET
        {
            SUCCESS = 0,
            FAILURE = 1,
            VIRTFILES_DB_ERROR = 2,
            VIRTFILES_ZIP_ERROR = 3,
            NOT_FOUND = 5,
            INVALID_PARAMETER = 6,
            FILE_CREATE_ERROR = 7,
            PE_RESOURCE_ERROR = 8,
            MEMORY_ERROR = 9,
            COMMIT_ERROR = 10,
            VIRTREG_DEPLOY_ERROR = 11,
            OUTPUT_ERROR = 12,
            INSUFFICIENT_BUFFER = 13,
            LOADLIBRARY_ERROR = 14,
            VIRTFILES_INI_ERROR = 15,
            APP_NOT_DEPLOYED = 16,
            INSUFFICIENT_PRIVILEGES = 17,
            _32_64_BIT_MISMATCH = 18,
            DOTNET_REQUIRED = 19,
            CANCELLED = 20,
            INJECTION_FAILED = 21,
            OLD_VERSION = 22,
            PASSWORD_REQUIRED = 23,
            PASSWORD_MISMATCH = 24,
        }

        public const int SANDBOXFLAGS_PASSTHROUGH = 1;
        public const int SANDBOXFLAGS_COPY_ON_WRITE = 2;
        public const int SANDBOXFLAGS_STRICTLY_ISOLATED = 3;

        public const int ISOLATIONMODE_CUSTOM = 0;
        public const int ISOLATIONMODE_ISOLATED = 1;
        public const int ISOLATIONMODE_FULL_ACCESS = 2;
        public const int ISOLATIONMODE_DATA = 3;

        private const String DLL32_v1 = "1.x\\PackagerDll.dll";
        private const String DLL32_v2 = "2.x\\PackagerDll.dll";
        private const String DLL64_v1 = "1.x\\PackagerDLL64.dll";
        private const String DLL64_v2 = "2.x\\PackagerDLL64.dll";
        public const int MAX_STRING = 64 * 1024;

        private IntPtr hPkg;
        public bool opened;
        public String openedFile;
        private static int ver = 2;
        public static int PkgVer{
            get{return ver;}
            set{ver=value;}
        }

        private const int MAX_PATH = 260;
        public const int MAX_APPID_LENGTH = 128;

        public const int LICENSETYPE_PRO = 2;
        public const int LICENSETYPE_DEV = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            [MarshalAs(UnmanagedType.U2)]
            public short Year;
            [MarshalAs(UnmanagedType.U2)]
            public short Month;
            [MarshalAs(UnmanagedType.U2)]
            public short DayOfWeek;
            [MarshalAs(UnmanagedType.U2)]
            public short Day;
            [MarshalAs(UnmanagedType.U2)]
            public short Hour;
            [MarshalAs(UnmanagedType.U2)]
            public short Minute;
            [MarshalAs(UnmanagedType.U2)]
            public short Second;
            [MarshalAs(UnmanagedType.U2)]
            public short Milliseconds;
        }

        //
        // DLL imports

        // PackageOpen
        [DllImport(DLL32_v1, EntryPoint="PackageOpen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageOpen32(
            String PackageExeFile,
            UInt32 Reserved,
            ref IntPtr hPkg);
        [DllImport(DLL32_v2, EntryPoint="PackageOpen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageOpen32_v2(
            String PackageExeFile,
            UInt32 Reserved,
            ref IntPtr hPkg);
        [DllImport(DLL64_v1, EntryPoint = "PackageOpen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageOpen64(
            String PackageExeFile,
            UInt32 Reserved,
            ref IntPtr hPkg);
        [DllImport(DLL64_v2, EntryPoint = "PackageOpen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageOpen64_v2(
            String PackageExeFile,
            UInt32 Reserved,
            ref IntPtr hPkg);
        private static int PackageOpen(
            String PackageExeFile,
            UInt32 Reserved,
            ref IntPtr hPkg)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageOpen32(PackageExeFile, Reserved, ref hPkg)  : PackageOpen32_v2(PackageExeFile, Reserved, ref hPkg) ):(PkgVer == 1 ? PackageOpen64(PackageExeFile, Reserved, ref hPkg) : PackageOpen64_v2(PackageExeFile, Reserved, ref hPkg));
        }

        // PackageCreate
        [DllImport(DLL32_v1, EntryPoint="PackageCreate", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageCreate32(
            String AppID,
            String AppVirtDll,
            String LoaderExe,
            ref IntPtr hPkg);
        [DllImport(DLL32_v2, EntryPoint = "PackageCreate", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageCreate32_v2(
            String AppID,
            String AppVirtDll,
            String LoaderExe,
            ref IntPtr hPkg);
        [DllImport(DLL64_v1, EntryPoint = "PackageCreate", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageCreate64(
            String AppID,
            String AppVirtDll,
            String LoaderExe,
            ref IntPtr hPkg);
        [DllImport(DLL64_v2, EntryPoint = "PackageCreate", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageCreate64_v2(
            String AppID,
            String AppVirtDll,
            String LoaderExe,
            ref IntPtr hPkg);
        private static int PackageCreate(
            String AppID,
            String AppVirtDll,
            String LoaderExe,
            ref IntPtr hPkg)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageCreate32(AppID, AppVirtDll, LoaderExe, ref hPkg) : PackageCreate32_v2(AppID, AppVirtDll, LoaderExe, ref hPkg)) : (PkgVer == 1 ? PackageCreate64(AppID, AppVirtDll, LoaderExe, ref hPkg) : PackageCreate64_v2(AppID, AppVirtDll, LoaderExe, ref hPkg));
        }

        // PackageClose
        [DllImport(DLL32_v1, EntryPoint = "PackageClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static void PackageClose32(
            IntPtr hPkg);
        [DllImport(DLL32_v2, EntryPoint = "PackageClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static void PackageClose32_v2(
            IntPtr hPkg);
        [DllImport(DLL64_v1, EntryPoint = "PackageClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static void PackageClose64(
            IntPtr hPkg);
        [DllImport(DLL64_v2, EntryPoint = "PackageClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static void PackageClose64_v2(
            IntPtr hPkg);
        private static void PackageClose(
            IntPtr hPkg)
        {
            if (Is32Bit())
                if (PkgVer == 1) PackageClose32(hPkg);
                else PackageClose32_v2(hPkg);
            else
                if (PkgVer == 1) PackageClose64(hPkg);
                else PackageClose64_v2(hPkg);
        }

        // PackageSave
        [DllImport(DLL32_v1, EntryPoint="PackageSave", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSave32(
            IntPtr hPkg,
            String OutFileName);
        [DllImport(DLL32_v2, EntryPoint="PackageSave", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSave32_v2(
            IntPtr hPkg,
            String OutFileName);
        [DllImport(DLL64_v1, EntryPoint = "PackageSave", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSave64(
            IntPtr hPkg,
            String OutFileName);
        [DllImport(DLL64_v2, EntryPoint = "PackageSave", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSave64_v2(
            IntPtr hPkg,
            String OutFileName);
        private static int PackageSave(
            IntPtr hPkg,
            String OutFileName)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageSave32(hPkg, OutFileName)  : PackageSave32_v2(hPkg, OutFileName) ):(PkgVer == 1 ? PackageSave64(hPkg, OutFileName) : PackageSave64_v2(hPkg, OutFileName));
        }

        // PackageGetProperty
        [DllImport(DLL32_v1, EntryPoint="PackageGetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageGetProperty32(
            IntPtr hPkg,
            String Name,
            StringBuilder Value,
            UInt32 ValueLen);
        [DllImport(DLL32_v2, EntryPoint="PackageGetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageGetProperty32_v2(
            IntPtr hPkg,
            String Name,
            StringBuilder Value,
            UInt32 ValueLen);
        [DllImport(DLL64_v1, EntryPoint = "PackageGetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageGetProperty64(
            IntPtr hPkg,
            String Name,
            StringBuilder Value,
            UInt32 ValueLen);
        [DllImport(DLL64_v2, EntryPoint = "PackageGetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageGetProperty64_v2(
            IntPtr hPkg,
            String Name,
            StringBuilder Value,
            UInt32 ValueLen);
        private static int PackageGetProperty(
            IntPtr hPkg,
            String Name,
            StringBuilder Value,
            UInt32 ValueLen)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageGetProperty32(hPkg, Name, Value, ValueLen)  : PackageGetProperty32_v2(hPkg, Name, Value, ValueLen) ):(PkgVer == 1 ? PackageGetProperty64(hPkg, Name, Value, ValueLen) : PackageGetProperty64_v2(hPkg, Name, Value, ValueLen));
        }

        // PackageSetProperty
        [DllImport(DLL32_v1, EntryPoint="PackageSetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProperty32(
            IntPtr hPkg,
            String Name,
            String Value);
        [DllImport(DLL32_v2, EntryPoint="PackageSetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProperty32_v2(
            IntPtr hPkg,
            String Name,
            String Value);
        [DllImport(DLL64_v1, EntryPoint = "PackageSetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProperty64(
            IntPtr hPkg,
            String Name,
            String Value);
        [DllImport(DLL64_v2, EntryPoint = "PackageSetProperty", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProperty64_v2(
            IntPtr hPkg,
            String Name,
            String Value);
        private static int PackageSetProperty(
            IntPtr hPkg,
            String Name,
            String Value)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageSetProperty32(hPkg, Name, Value)  : PackageSetProperty32_v2(hPkg, Name, Value) ):(PkgVer == 1 ? PackageSetProperty64(hPkg, Name, Value) : PackageSetProperty64_v2(hPkg, Name, Value));
        }

        // PackageSetIconFile
        [DllImport(DLL32_v1, EntryPoint="PackageSetIconFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetIconFile32(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL32_v2, EntryPoint="PackageSetIconFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetIconFile32_v2(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v1, EntryPoint = "PackageSetIconFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetIconFile64(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v2, EntryPoint = "PackageSetIconFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetIconFile64_v2(
            IntPtr hPkg,
            String FileName);
        private static int PackageSetIconFile(
            IntPtr hPkg,
            String FileName)
        {
            return Is32Bit() ? (PkgVer == 1 ? PackageSetIconFile32(hPkg, FileName)  : PackageSetIconFile32_v2(hPkg, FileName) ):(PkgVer == 1 ? PackageSetIconFile64(hPkg, FileName) : PackageSetIconFile64_v2(hPkg, FileName));
        }

        // PackageSetProtection
        [DllImport(DLL32_v2, EntryPoint = "PackageSetProtection", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProtection32(
            IntPtr hPkg,
            [MarshalAs(UnmanagedType.LPStr)] String Password,
            Int32 ProtectedActions,
            String RequireCertificate);
        [DllImport(DLL64_v2, EntryPoint = "PackageSetProtection", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackageSetProtection64(
            IntPtr hPkg,
            [MarshalAs(UnmanagedType.LPStr)] String Password,
            Int32 ProtectedActions,
            String RequireCertificate);
        private static int PackageSetProtection(
            IntPtr hPkg,
            [MarshalAs(UnmanagedType.LPStr)] String Password,
            Int32 ProtectedActions,
            String RequireCertificate)
        {
			if (PkgVer == 1) return (int)APIRET.SUCCESS;
            return Is32Bit() ? PackageSetProtection32(hPkg, Password, ProtectedActions, RequireCertificate) : PackageSetProtection64(hPkg, Password, ProtectedActions, RequireCertificate);
        }

        //
        // VirtFs imports
        private delegate bool VIRTFS_ENUM_CALLBACK(
            ref Object Data,
            [MarshalAs(UnmanagedType.LPWStr)] String FileName,
            UInt32 FileFlags,
            UInt64 CreationTime,
            UInt64 LastAccessTime,
            UInt64 LastWriteTime,
            UInt64 ChangeTime,
            UInt64 EndOfFile,
            UInt32 FileAttributes);
        [DllImport(DLL32_v1, EntryPoint="VirtFsEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsEnum32(
            IntPtr hPkg,
            VIRTFS_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL32_v2, EntryPoint="VirtFsEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsEnum32_v2(
            IntPtr hPkg,
            VIRTFS_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsEnum64(
            IntPtr hPkg,
            VIRTFS_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsEnum64_v2(
            IntPtr hPkg,
            VIRTFS_ENUM_CALLBACK Callback,
            ref Object Data);
        private static int VirtFsEnum(
            IntPtr hPkg,
            VIRTFS_ENUM_CALLBACK Callback,
            ref Object Data)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsEnum32(hPkg, Callback, ref Data)  : VirtFsEnum32_v2(hPkg, Callback, ref Data) ):(PkgVer == 1 ? VirtFsEnum64(hPkg, Callback, ref Data) : VirtFsEnum64_v2(hPkg, Callback, ref Data));
        }

        // VirtFsAdd
        [DllImport(DLL32_v1, EntryPoint="VirtFsAdd", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAdd32(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName);
        [DllImport(DLL32_v2, EntryPoint="VirtFsAdd", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAdd32_v2(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsAdd", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAdd64(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsAdd", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAdd64_v2(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName);
        private static int VirtFsAdd(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsAdd32(hPkg, SrcFileName, DestFileName, bVariablizeName)  : VirtFsAdd32_v2(hPkg, SrcFileName, DestFileName, bVariablizeName) ):(PkgVer == 1 ? VirtFsAdd64(hPkg, SrcFileName, DestFileName, bVariablizeName) : VirtFsAdd64_v2(hPkg, SrcFileName, DestFileName, bVariablizeName));
        }

        // VirtFsAddEx
        [DllImport(DLL32_v1, EntryPoint="VirtFsAddEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEx32(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            UInt32 FileFlags);
        [DllImport(DLL32_v2, EntryPoint="VirtFsAddEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEx32_v2(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            UInt32 FileFlags);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsAddEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEx64(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            UInt32 FileFlags);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsAddEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEx64_v2(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            UInt32 FileFlags);
        private static int VirtFsAddEx(
            IntPtr hPkg,
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            UInt32 FileFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsAddEx32(hPkg, SrcFileName, DestFileName, bVariablizeName, FileFlags)  : VirtFsAddEx32_v2(hPkg, SrcFileName, DestFileName, bVariablizeName, FileFlags) ):(PkgVer == 1 ? VirtFsAddEx64(hPkg, SrcFileName, DestFileName, bVariablizeName, FileFlags) : VirtFsAddEx64_v2(hPkg, SrcFileName, DestFileName, bVariablizeName, FileFlags));
        }

        // VirtFsAddEmptyDir
        [DllImport(DLL32_v1, EntryPoint="VirtFsAddEmptyDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEmptyDir32(
            IntPtr hPkg,
            String DirName,
            bool bVariablizeName);
        [DllImport(DLL32_v2, EntryPoint="VirtFsAddEmptyDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEmptyDir32_v2(
            IntPtr hPkg,
            String DirName,
            bool bVariablizeName);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsAddEmptyDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEmptyDir64(
            IntPtr hPkg,
            String DirName,
            bool bVariablizeName);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsAddEmptyDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsAddEmptyDir64_v2(
            IntPtr hPkg,
            String DirName,
            bool bVariablizeName);
        private static int VirtFsAddEmptyDir(
            IntPtr hPkg,
            String DirName,
            bool bVariablizeName)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsAddEmptyDir32(hPkg, DirName, bVariablizeName)  : VirtFsAddEmptyDir32_v2(hPkg, DirName, bVariablizeName) ):(PkgVer == 1 ? VirtFsAddEmptyDir64(hPkg, DirName, bVariablizeName) : VirtFsAddEmptyDir64_v2(hPkg, DirName, bVariablizeName));
        }

        // VirtFsExtract
        [DllImport(DLL32_v1, EntryPoint="VirtFsExtract", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsExtract32(
            IntPtr hPkg,
            String FileName,
            String TargetDir);
        [DllImport(DLL32_v2, EntryPoint="VirtFsExtract", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsExtract32_v2(
            IntPtr hPkg,
            String FileName,
            String TargetDir);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsExtract", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsExtract64(
            IntPtr hPkg,
            String FileName,
            String TargetDir);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsExtract", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsExtract64_v2(
            IntPtr hPkg,
            String FileName,
            String TargetDir);
        private static int VirtFsExtract(
            IntPtr hPkg,
            String FileName,
            String TargetDir)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsExtract32(hPkg, FileName, TargetDir)  : VirtFsExtract32_v2(hPkg, FileName, TargetDir) ):(PkgVer == 1 ? VirtFsExtract64(hPkg, FileName, TargetDir) : VirtFsExtract64_v2(hPkg, FileName, TargetDir));
        }

        // VirtFsDelete
        [DllImport(DLL32_v1, EntryPoint = "VirtFsDelete", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsDelete32(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL32_v2, EntryPoint = "VirtFsDelete", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsDelete32_v2(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsDelete", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsDelete64(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsDelete", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsDelete64_v2(
            IntPtr hPkg,
            String FileName);
        private static int VirtFsDelete(
            IntPtr hPkg,
            String FileName)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsDelete32(hPkg, FileName)  : VirtFsDelete32_v2(hPkg, FileName) ):(PkgVer == 1 ? VirtFsDelete64(hPkg, FileName) : VirtFsDelete64_v2(hPkg, FileName));
        }

        // VirtFsSetFileStreaming
        [DllImport(DLL32_v1, EntryPoint="VirtFsSetFileStreaming", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileStreaming32(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL32_v2, EntryPoint="VirtFsSetFileStreaming", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileStreaming32_v2(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsSetFileStreaming", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileStreaming64(
            IntPtr hPkg,
            String FileName);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsSetFileStreaming", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileStreaming64_v2(
            IntPtr hPkg,
            String FileName);
        public static int VirtFsSetFileStreaming(
            IntPtr hPkg,
            String FileName)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsSetFileStreaming32(hPkg, FileName)  : VirtFsSetFileStreaming32_v2(hPkg, FileName) ):(PkgVer == 1 ? VirtFsSetFileStreaming64(hPkg, FileName) : VirtFsSetFileStreaming64_v2(hPkg, FileName));
        }


        //
        // VirtReg imports

        // VirtRegGetWorkKey
        [DllImport(DLL32_v1, EntryPoint="VirtRegGetWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKey32(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen);
        [DllImport(DLL32_v2, EntryPoint="VirtRegGetWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKey32_v2(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen);
        [DllImport(DLL32_v1, EntryPoint = "VirtRegGetWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKey64(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen);
        [DllImport(DLL32_v2, EntryPoint = "VirtRegGetWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKey64_v2(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen);
        private static int VirtRegGetWorkKey(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtRegGetWorkKey32(hPkg, WorkKey, WorkKeyLen)  : VirtRegGetWorkKey32_v2(hPkg, WorkKey, WorkKeyLen) ):(PkgVer == 1 ? VirtRegGetWorkKey64(hPkg, WorkKey, WorkKeyLen) : VirtRegGetWorkKey64_v2(hPkg, WorkKey, WorkKeyLen));
        }

        // VirtRegGetWorkKeyEx
        [DllImport(DLL32_v1, EntryPoint="VirtRegGetWorkKeyEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKeyEx32(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen,
            SafeWaitHandle hAbortEvent);
        [DllImport(DLL32_v2, EntryPoint="VirtRegGetWorkKeyEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKeyEx32_v2(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen,
            SafeWaitHandle hAbortEvent);
        [DllImport(DLL64_v1, EntryPoint = "VirtRegGetWorkKeyEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKeyEx64(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen,
            SafeWaitHandle hAbortEvent);
        [DllImport(DLL64_v2, EntryPoint = "VirtRegGetWorkKeyEx", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegGetWorkKeyEx64_v2(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen,
            SafeWaitHandle hAbortEvent);
        private static int VirtRegGetWorkKeyEx(
            IntPtr hPkg,
            StringBuilder WorkKey,
            UInt32 WorkKeyLen,
            SafeWaitHandle hAbortEvent)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtRegGetWorkKeyEx32(hPkg, WorkKey, WorkKeyLen, hAbortEvent)  : VirtRegGetWorkKeyEx32_v2(hPkg, WorkKey, WorkKeyLen, hAbortEvent) ):(PkgVer == 1 ? VirtRegGetWorkKeyEx64(hPkg, WorkKey, WorkKeyLen, hAbortEvent) : VirtRegGetWorkKeyEx64_v2(hPkg, WorkKey, WorkKeyLen, hAbortEvent));
        }

        // VirtRegSaveWorkKey
        [DllImport(DLL32_v1, EntryPoint="VirtRegSaveWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegSaveWorkKey32(
            IntPtr hPkg);
        [DllImport(DLL32_v2, EntryPoint="VirtRegSaveWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegSaveWorkKey32_v2(
            IntPtr hPkg);
        [DllImport(DLL64_v1, EntryPoint = "VirtRegSaveWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegSaveWorkKey64(
            IntPtr hPkg);
        [DllImport(DLL64_v2, EntryPoint = "VirtRegSaveWorkKey", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtRegSaveWorkKey64_v2(
            IntPtr hPkg);
        private static int VirtRegSaveWorkKey(
            IntPtr hPkg)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtRegSaveWorkKey32(hPkg)  : VirtRegSaveWorkKey32_v2(hPkg) ):(PkgVer == 1 ? VirtRegSaveWorkKey64(hPkg) : VirtRegSaveWorkKey64_v2(hPkg));
        }


        //
        // Sandbox imports

        // SandboxGetRegistryFlags
        [DllImport(DLL32_v1, EntryPoint="SandboxGetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetRegistryFlags32(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL32_v2, EntryPoint="SandboxGetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetRegistryFlags32_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL64_v1, EntryPoint = "SandboxGetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetRegistryFlags64(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL64_v2, EntryPoint = "SandboxGetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetRegistryFlags64_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        private static int SandboxGetRegistryFlags(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? SandboxGetRegistryFlags32(hPkg, Path, bVariablizeName, ref SandboxFlags)  : SandboxGetRegistryFlags32_v2(hPkg, Path, bVariablizeName, ref SandboxFlags) ):(PkgVer == 1 ? SandboxGetRegistryFlags64(hPkg, Path, bVariablizeName, ref SandboxFlags) : SandboxGetRegistryFlags64_v2(hPkg, Path, bVariablizeName, ref SandboxFlags));
        }

        // SandboxGetFileFlags
        [DllImport(DLL32_v1, EntryPoint="SandboxGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetFileFlags32(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL32_v2, EntryPoint="SandboxGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetFileFlags32_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL64_v1, EntryPoint = "SandboxGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetFileFlags64(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        [DllImport(DLL64_v2, EntryPoint = "SandboxGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxGetFileFlags64_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags);
        private static int SandboxGetFileFlags(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            ref UInt32 SandboxFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? SandboxGetFileFlags32(hPkg, Path, bVariablizeName, ref SandboxFlags)  : SandboxGetFileFlags32_v2(hPkg, Path, bVariablizeName, ref SandboxFlags) ):(PkgVer == 1 ? SandboxGetFileFlags64(hPkg, Path, bVariablizeName, ref SandboxFlags) : SandboxGetFileFlags64_v2(hPkg, Path, bVariablizeName, ref SandboxFlags));
        }

        // SandboxSetRegistryFlags
        [DllImport(DLL32_v1, EntryPoint="SandboxSetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetRegistryFlags32(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL32_v2, EntryPoint="SandboxSetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetRegistryFlags32_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL64_v1, EntryPoint = "SandboxSetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetRegistryFlags64(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL64_v2, EntryPoint = "SandboxSetRegistryFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetRegistryFlags64_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        private static int SandboxSetRegistryFlags(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? SandboxSetRegistryFlags32(hPkg, Path, bVariablizeName, SandboxFlags)  : SandboxSetRegistryFlags32_v2(hPkg, Path, bVariablizeName, SandboxFlags) ):(PkgVer == 1 ? SandboxSetRegistryFlags64(hPkg, Path, bVariablizeName, SandboxFlags) : SandboxSetRegistryFlags64_v2(hPkg, Path, bVariablizeName, SandboxFlags));
        }

        // SandboxSetFileFlags
        [DllImport(DLL32_v1, EntryPoint="SandboxSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetFileFlags32(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL32_v2, EntryPoint="SandboxSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetFileFlags32_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL64_v1, EntryPoint = "SandboxSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetFileFlags64(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        [DllImport(DLL64_v2, EntryPoint = "SandboxSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int SandboxSetFileFlags64_v2(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags);
        private static int SandboxSetFileFlags(
            IntPtr hPkg,
            String Path,
            bool bVariablizeName,
            UInt32 SandboxFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? SandboxSetFileFlags32(hPkg, Path, bVariablizeName, SandboxFlags)  : SandboxSetFileFlags32_v2(hPkg, Path, bVariablizeName, SandboxFlags) ):(PkgVer == 1 ? SandboxSetFileFlags64(hPkg, Path, bVariablizeName, SandboxFlags) : SandboxSetFileFlags64_v2(hPkg, Path, bVariablizeName, SandboxFlags));
        }

        // VirtFsGetFileFlags
        [DllImport(DLL32_v1, EntryPoint="VirtFsGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsGetFileFlags32(
            IntPtr hPkg,
            String Path,
            ref UInt32 FileFlags);
        [DllImport(DLL32_v2, EntryPoint="VirtFsGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsGetFileFlags32_v2(
            IntPtr hPkg,
            String Path,
            ref UInt32 FileFlags);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsGetFileFlags64(
            IntPtr hPkg,
            String Path,
            ref UInt32 FileFlags);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsGetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsGetFileFlags64_v2(
            IntPtr hPkg,
            String Path,
            ref UInt32 FileFlags);
        private static int VirtFsGetFileFlags(
            IntPtr hPkg,
            String Path,
            ref UInt32 FileFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsGetFileFlags32(hPkg, Path, ref FileFlags)  : VirtFsGetFileFlags32_v2(hPkg, Path, ref FileFlags) ):(PkgVer == 1 ? VirtFsGetFileFlags64(hPkg, Path, ref FileFlags) : VirtFsGetFileFlags64_v2(hPkg, Path, ref FileFlags));
        }

        // VirtFsSetFileFlags
        [DllImport(DLL32_v1, EntryPoint="VirtFsSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileFlags32(
            IntPtr hPkg,
            String Path,
            UInt32 FileFlags);
        [DllImport(DLL32_v2, EntryPoint="VirtFsSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileFlags32_v2(
            IntPtr hPkg,
            String Path,
            UInt32 FileFlags);
        [DllImport(DLL64_v1, EntryPoint = "VirtFsSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileFlags64(
            IntPtr hPkg,
            String Path,
            UInt32 FileFlags);
        [DllImport(DLL64_v2, EntryPoint = "VirtFsSetFileFlags", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int VirtFsSetFileFlags64_v2(
            IntPtr hPkg,
            String Path,
            UInt32 FileFlags);
        private static int VirtFsSetFileFlags(
            IntPtr hPkg,
            String Path,
            UInt32 FileFlags)
        {
            return Is32Bit() ? (PkgVer == 1 ? VirtFsSetFileFlags32(hPkg, Path, FileFlags)  : VirtFsSetFileFlags32_v2(hPkg, Path, FileFlags) ):(PkgVer == 1 ? VirtFsSetFileFlags64(hPkg, Path, FileFlags) : VirtFsSetFileFlags64_v2(hPkg, Path, FileFlags));
        }


        //
        // 'Quick' functions (do not require package to be opened, can be called on closed package files)

        // QuickReadIni
        [DllImport(DLL32_v1, EntryPoint="QuickReadIni", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickReadIni32(
            String PackageExeFile,
            StringBuilder IniBuf,
            UInt32 IniBufLen);
        [DllImport(DLL32_v2, EntryPoint="QuickReadIni", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickReadIni32_v2(
            String PackageExeFile,
            StringBuilder IniBuf,
            UInt32 IniBufLen);
        [DllImport(DLL64_v1, EntryPoint = "QuickReadIni", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickReadIni64(
            String PackageExeFile,
            StringBuilder IniBuf,
            UInt32 IniBufLen);
        [DllImport(DLL64_v2, EntryPoint = "QuickReadIni", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickReadIni64_v2(
            String PackageExeFile,
            StringBuilder IniBuf,
            UInt32 IniBufLen);
        public static int QuickReadIni(
            String PackageExeFile,
            StringBuilder IniBuf,
            UInt32 IniBufLen)
        {
            return Is32Bit() ? (PkgVer == 1 ? QuickReadIni32(PackageExeFile, IniBuf, IniBufLen)  : QuickReadIni32_v2(PackageExeFile, IniBuf, IniBufLen) ):(PkgVer == 1 ? QuickReadIni64(PackageExeFile, IniBuf, IniBufLen) : QuickReadIni64_v2(PackageExeFile, IniBuf, IniBufLen));
        }

        // QuickReadIniValues (wrapper)
        public static Hashtable QuickReadIniValues(string PacakgeExeFile)
        {
            StringBuilder sb = new StringBuilder(16384);
            VirtPackage.QuickReadIni(PacakgeExeFile, sb, 16384);
            return VirtPackage.ReadIniSettingsBuf(sb.ToString());
        }

        // QuickBuildAppendiceIndex (reserved for internal use)
        [DllImport(DLL32_v1, EntryPoint = "QuickBuildAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickBuildAppendiceIndex32(
            byte[] pLastFileBytes,
            UInt32 cbLastFileBytes,
            byte[] pNewIndex,
            ref UInt32 pcbNewIndex,
            byte[] pSandboxCfg,
            UInt32 cbSandboxCfg,
            byte[] pIniBuf,
            UInt32 cbIniBuf);
        [DllImport(DLL32_v2, EntryPoint = "QuickBuildAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickBuildAppendiceIndex32_v2(
            byte[] pLastFileBytes,
            UInt32 cbLastFileBytes,
            byte[] pNewIndex,
            ref UInt32 pcbNewIndex,
            byte[] pSandboxCfg,
            UInt32 cbSandboxCfg,
            byte[] pIniBuf,
            UInt32 cbIniBuf);
        [DllImport(DLL64_v1, EntryPoint = "QuickBuildAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickBuildAppendiceIndex64(
            byte[] pLastFileBytes,
            UInt32 cbLastFileBytes,
            byte[] pNewIndex,
            ref UInt32 pcbNewIndex,
            byte[] pSandboxCfg,
            UInt32 cbSandboxCfg,
            byte[] pIniBuf,
            UInt32 cbIniBuf);
        [DllImport(DLL64_v2, EntryPoint = "QuickBuildAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickBuildAppendiceIndex64_v2(
            byte[] pLastFileBytes,
            UInt32 cbLastFileBytes,
            byte[] pNewIndex,
            ref UInt32 pcbNewIndex,
            byte[] pSandboxCfg,
            UInt32 cbSandboxCfg,
            byte[] pIniBuf,
            UInt32 cbIniBuf);
        public static int QuickBuildAppendiceIndex(
            byte[] pLastFileBytes,
            UInt32 cbLastFileBytes,
            byte[] pNewIndex,
            ref UInt32 pcbNewIndex,
            byte[] pSandboxCfg,
            UInt32 cbSandboxCfg,
            byte[] pIniBuf,
            UInt32 cbIniBuf)
        {
            int ret = Is32Bit()
                ?(PkgVer == 1 ? QuickBuildAppendiceIndex32(pLastFileBytes, cbLastFileBytes, pNewIndex, ref pcbNewIndex, pSandboxCfg, cbSandboxCfg, pIniBuf, cbIniBuf) : QuickBuildAppendiceIndex32_v2(pLastFileBytes, cbLastFileBytes, pNewIndex, ref pcbNewIndex, pSandboxCfg, cbSandboxCfg, pIniBuf, cbIniBuf))
                :(PkgVer == 1 ? QuickBuildAppendiceIndex64(pLastFileBytes, cbLastFileBytes, pNewIndex, ref pcbNewIndex, pSandboxCfg, cbSandboxCfg, pIniBuf, cbIniBuf) : QuickBuildAppendiceIndex64_v2(pLastFileBytes, cbLastFileBytes, pNewIndex, ref pcbNewIndex, pSandboxCfg, cbSandboxCfg, pIniBuf, cbIniBuf));
            return ret;
        }

        // QuickExtractAppendiceIndex (reserved for internal use)
        [DllImport(DLL32_v1, EntryPoint = "QuickExtractAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickExtractAppendiceIndex32(
            String FileName,
            String OutputFile);
        [DllImport(DLL32_v2, EntryPoint = "QuickExtractAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickExtractAppendiceIndex32_v2(
            String FileName,
            String OutputFile);
        [DllImport(DLL64_v1, EntryPoint = "QuickExtractAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickExtractAppendiceIndex64(
            String FileName,
            String OutputFile);
        [DllImport(DLL64_v2, EntryPoint = "QuickExtractAppendiceIndex", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int QuickExtractAppendiceIndex64_v2(
            String FileName,
            String OutputFile);
        public static int QuickExtractAppendiceIndex(
            String FileName,
            String OutputFile)
        {
            return Is32Bit() ? (PkgVer == 1 ? QuickExtractAppendiceIndex32(FileName, OutputFile)  : QuickExtractAppendiceIndex32_v2(FileName, OutputFile) ):(PkgVer == 1 ? QuickExtractAppendiceIndex64(FileName, OutputFile) : QuickExtractAppendiceIndex64_v2(FileName, OutputFile));
        }
        static public byte[] QuickExtractAppendiceIndexBytes(
            String FileName)
        {
            string outputFile = Path.GetTempFileName();
            try { File.Delete(outputFile); }
            catch { }
            if (VirtPackage.QuickExtractAppendiceIndex(FileName, outputFile) != (int)VirtPackage.APIRET.SUCCESS)
                return null;
            byte[] ret = File.ReadAllBytes(outputFile);
            try { File.Delete(outputFile); }
            catch { }
            return ret;
        }

        // LicDataLoadFromFile (reserved for internal use)
        [DllImport(DLL32_v2, EntryPoint = "LicDataLoadFromFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int LicDataLoadFromFile32(
            String FileName);
        [DllImport(DLL64_v2, EntryPoint = "LicDataLoadFromFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int LicDataLoadFromFile64(
            String FileName);
        public static int LicDataLoadFromFile(
            String FileName)
        {
            if (PkgVer == 1) return 3;
            int ret = Is32Bit()
                ? LicDataLoadFromFile32(FileName)
                : LicDataLoadFromFile64(FileName);
            return ret;
        }


        //
        // DeployedApp imports

        private delegate bool DEPLOYEDAPP_ENUM_CALLBACK(
            ref Object Data,
            [MarshalAs(UnmanagedType.LPWStr)] String AppID);

        // DeployedAppEnum
        [DllImport(DLL32_v1, EntryPoint="DeployedAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppEnum32(
            DEPLOYEDAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL32_v2, EntryPoint="DeployedAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppEnum32_v2(
            DEPLOYEDAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v1, EntryPoint = "DeployedAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppEnum64(
            DEPLOYEDAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v2, EntryPoint = "DeployedAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppEnum64_v2(
            DEPLOYEDAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        private static int DeployedAppEnum(
            DEPLOYEDAPP_ENUM_CALLBACK Callback,
            ref Object Data)
        {
            return Is32Bit() ? (PkgVer == 1 ? DeployedAppEnum32(Callback, ref Data) : DeployedAppEnum32_v2(Callback, ref Data) ):(PkgVer == 1 ? DeployedAppEnum64(Callback, ref Data) : DeployedAppEnum64_v2(Callback, ref Data));
        }

        // DeployedAppGetDir
        [DllImport(DLL32_v1, EntryPoint="DeployedAppGetDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppGetDir32(
            String AppID,
            StringBuilder BaseDirName,
            UInt32 BaseDirNameLen);
        [DllImport(DLL32_v2, EntryPoint="DeployedAppGetDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppGetDir32_v2(
            String AppID,
            StringBuilder BaseDirName,
            UInt32 BaseDirNameLen);
        [DllImport(DLL64_v1, EntryPoint = "DeployedAppGetDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppGetDir64(
            String AppID,
            StringBuilder BaseDirName,
            UInt32 BaseDirNameLen);
        [DllImport(DLL64_v2, EntryPoint = "DeployedAppGetDir", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int DeployedAppGetDir64_v2(
            String AppID,
            StringBuilder BaseDirName,
            UInt32 BaseDirNameLen);
        private static int DeployedAppGetDir(
            String AppID,
            StringBuilder BaseDirName,
            UInt32 BaseDirNameLen)
        {
            return Is32Bit() ? (PkgVer == 1 ? DeployedAppGetDir32(AppID, BaseDirName, BaseDirNameLen)  : DeployedAppGetDir32_v2(AppID, BaseDirName, BaseDirNameLen) ):(PkgVer == 1 ? DeployedAppGetDir64(AppID, BaseDirName, BaseDirNameLen) : DeployedAppGetDir64_v2(AppID, BaseDirName, BaseDirNameLen));
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VIRT_PROCESS
        {
            public UInt32 PID;
            public UInt32 Flags;
        }


        //
        // RunningApp imports

        // RunningAppEnum
        [DllImport(DLL32_v1, EntryPoint = "RunningAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnum32(
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL32_v2, EntryPoint = "RunningAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnum32_v2(
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v1, EntryPoint = "RunningAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnum64(
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v2, EntryPoint = "RunningAppEnum", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnum64_v2(
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        private static int RunningAppEnum(
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data)
        {
            return Is32Bit() ? (PkgVer == 1 ? RunningAppEnum32(Callback, ref Data)  : RunningAppEnum32_v2(Callback, ref Data) ):(PkgVer == 1 ? RunningAppEnum64(Callback, ref Data) : RunningAppEnum64_v2(Callback, ref Data));
        }

        // RunningAppEnumKeepAlive
        [DllImport(DLL32_v1, EntryPoint = "RunningAppEnumKeepAlive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAlive32(
            ref IntPtr Context,
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL32_v2, EntryPoint = "RunningAppEnumKeepAlive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAlive32_v2(
            ref IntPtr Context,
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v1, EntryPoint = "RunningAppEnumKeepAlive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAlive64(
            ref IntPtr Context,
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        [DllImport(DLL64_v2, EntryPoint = "RunningAppEnumKeepAlive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAlive64_v2(
            ref IntPtr Context,
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data);
        private static int RunningAppEnumKeepAlive(
            ref IntPtr Context,
            RUNNINGAPP_ENUM_CALLBACK Callback,
            ref Object Data)
        {
            return Is32Bit() ? (PkgVer == 1 ? RunningAppEnumKeepAlive32(ref Context, Callback, ref Data)  : RunningAppEnumKeepAlive32_v2(ref Context, Callback, ref Data) ):(PkgVer == 1 ? RunningAppEnumKeepAlive64(ref Context, Callback, ref Data) : RunningAppEnumKeepAlive64_v2(ref Context, Callback, ref Data));
        }

        // RunningAppEnumKeepAliveFree
        [DllImport(DLL32_v1, EntryPoint = "RunningAppEnumKeepAliveFree", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAliveFree32(
            IntPtr Context);
        [DllImport(DLL32_v2, EntryPoint = "RunningAppEnumKeepAliveFree", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAliveFree32_v2(
            IntPtr Context);
        [DllImport(DLL64_v1, EntryPoint = "RunningAppEnumKeepAliveFree", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAliveFree64(
            IntPtr Context);
        [DllImport(DLL64_v2, EntryPoint = "RunningAppEnumKeepAliveFree", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int RunningAppEnumKeepAliveFree64_v2(
            IntPtr Context);
        private static int RunningAppEnumKeepAliveFree(
            IntPtr Context)
        {
            return Is32Bit() ? (PkgVer == 1 ? RunningAppEnumKeepAliveFree32(Context)  : RunningAppEnumKeepAliveFree32_v2(Context) ):(PkgVer == 1 ? RunningAppEnumKeepAliveFree64(Context) : RunningAppEnumKeepAliveFree64_v2(Context));
        }

        // PackUtils_CopyIconsFromExeToExe
        [DllImport(DLL32_v1, EntryPoint = "PackUtils_CopyIconsFromExeToExe", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackUtils_CopyIconsFromExeToExe32(
            String SrcFile,
            String TgtFile);
        [DllImport(DLL32_v2, EntryPoint = "PackUtils_CopyIconsFromExeToExe", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackUtils_CopyIconsFromExeToExe32_v2(
            String SrcFile,
            String TgtFile);
        [DllImport(DLL64_v1, EntryPoint = "PackUtils_CopyIconsFromExeToExe", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackUtils_CopyIconsFromExeToExe64(
            String SrcFile,
            String TgtFile);
        [DllImport(DLL64_v2, EntryPoint = "PackUtils_CopyIconsFromExeToExe", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int PackUtils_CopyIconsFromExeToExe64_v2(
            String SrcFile,
            String TgtFile);
        public static int PackUtils_CopyIconsFromExeToExe(
            String SrcFile,
            String TgtFile)
        {
            if (PkgVer == 1) return 1;
            return Is32Bit() ? (PkgVer == 1 ? PackUtils_CopyIconsFromExeToExe32(SrcFile, TgtFile)  : PackUtils_CopyIconsFromExeToExe32_v2(SrcFile, TgtFile) ):(PkgVer == 1 ? PackUtils_CopyIconsFromExeToExe64(SrcFile, TgtFile) : PackUtils_CopyIconsFromExeToExe64_v2(SrcFile, TgtFile));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RUNNING_APP
        {
            public UInt32 Version;
            public UInt32 SerialId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PATH * 2 * 2)]
            public char[] CarrierExeName;
            public UInt32 CarrierPID;
            public UInt32 StartTickTime;
            public UInt32 SyncStreamingDuration;
            public UInt32 TotalPIDs;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public VIRT_PROCESS[] Processes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_APPID_LENGTH * 2)]
            public char[] AppID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_APPID_LENGTH * 2)]
            public char[] FriendlyName;
        }
        public class RunningApp
        {
            public String AppID;
            public List<VIRT_PROCESS> Processes;
            public String CarrierExeName;
            public UInt32 SerialId;
            public UInt32 CarrierPID;
            public UInt32 StartTickTime;
            public String FriendlyName;

            public int GetVintegrationMode(out bool anyVirtProcessRunning)
            {
                int vintegration = 0;
                anyVirtProcessRunning = false;
                foreach (VirtPackage.VIRT_PROCESS process in this.Processes)
                {
                    if (((int)process.Flags & ((int)VIRT_PROCESS_FLAGS.VINTEGRATE_PROCESS_ONLY | (int)VIRT_PROCESS_FLAGS.VINTEGRATE_RECURSIVE)) != 0)
                        vintegration |= (int)process.Flags;
                    else
                        anyVirtProcessRunning = true;
                }
                return vintegration;
            }

            public static RunningApp FromAppID(String AppID)
            {
                List<RunningApp> runningApps = GetRunningApps();
                foreach (RunningApp runningApp in runningApps)
                {
                    if (runningApp.AppID.Equals(AppID, StringComparison.InvariantCultureIgnoreCase))
                        return runningApp;
                }
                return null;
            }
        }
        private delegate bool RUNNINGAPP_ENUM_CALLBACK(
            ref Object Data,
            ref RUNNING_APP RunningApp);


        //
        // Utils imports

        // UtilsGenericToLocalPath
        [DllImport(DLL32_v1, EntryPoint = "UtilsGenericToLocalPath", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int UtilsGenericToLocalPath32(
            String GenericPath,
            StringBuilder LocalPath,
            UInt32 LocalPathLen);
        [DllImport(DLL32_v2, EntryPoint = "UtilsGenericToLocalPath", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int UtilsGenericToLocalPath32_v2(
            String GenericPath,
            StringBuilder LocalPath,
            UInt32 LocalPathLen);
        [DllImport(DLL64_v1, EntryPoint = "UtilsGenericToLocalPath", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int UtilsGenericToLocalPath64(
            String GenericPath,
            StringBuilder LocalPath,
            UInt32 LocalPathLen);
        [DllImport(DLL64_v2, EntryPoint = "UtilsGenericToLocalPath", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private extern static int UtilsGenericToLocalPath64_v2(
            String GenericPath,
            StringBuilder LocalPath,
            UInt32 LocalPathLen);
        public static int UtilsGenericToLocalPath(
            String GenericPath,
            StringBuilder LocalPath,
            UInt32 LocalPathLen)
        {
            return Is32Bit() ? (PkgVer == 1 ? UtilsGenericToLocalPath32(GenericPath, LocalPath, LocalPathLen)  : UtilsGenericToLocalPath32_v2(GenericPath, LocalPath, LocalPathLen) ):(PkgVer == 1 ? UtilsGenericToLocalPath64(GenericPath, LocalPath, LocalPathLen) : UtilsGenericToLocalPath64_v2(GenericPath, LocalPath, LocalPathLen));
        }
        public static string GenericToLocalDir(string GenericPath)
        {
            //if (string.IsNullOrEmpty(genericPath))
            StringBuilder sbValue = new StringBuilder(VirtPackage.MAX_STRING);
            VirtPackage.APIRET Ret = (VirtPackage.APIRET)VirtPackage.UtilsGenericToLocalPath(GenericPath, sbValue, VirtPackage.MAX_STRING);
            return sbValue.ToString();
        }


        //
        // .NET wrapper
        public VirtPackage()
        {
            opened = false;
            openedFile = "";
            //private const String DLLNAME = "PackagerDll.dll";
        }

        ~VirtPackage()
        {
            Close();
        }

        static public bool Is32Bit()
        {
            return (IntPtr.Size == 4);
        }

        public bool Close()
        {
            if (opened)
            {
                PackageClose(hPkg);
                opened = false;
                openedFile = "";
            }
            return true;
        }

        public bool Save(String FileName)
        {
            APIRET apiRet;
            return SaveEx(FileName, out apiRet);
        }

        public bool SaveEx(String FileName, out APIRET apiRet)
        {
            int Ret = PackageSave(hPkg, FileName);
            apiRet = (APIRET)Ret;
            if (apiRet == APIRET.SUCCESS)
                return true;
            else
                return false;
        }

        public bool Open(String PackageExeFile)
        {
            APIRET apiRet;
            return Open(PackageExeFile, out apiRet);
        }

        public bool Open(String PackageExeFile, out APIRET apiRet)
        {
            apiRet = (APIRET)PackageOpen(PackageExeFile, 0, ref hPkg);
            if (apiRet == APIRET.SUCCESS)
            {
                opened = true;
                int passwordPos = PackageExeFile.IndexOf('|');
                if (passwordPos == -1)
                    openedFile = PackageExeFile;
                else
                    openedFile = PackageExeFile.Substring(0, passwordPos);
                return true;
            }
            else
                return false;
        }

        public bool Create(String AppID, String AppVirtDll, String LoaderExe)
        {
            APIRET Ret = (APIRET)PackageCreate(AppID, AppVirtDll, LoaderExe, ref hPkg);
            if (Ret == APIRET.SUCCESS)
            {
                opened = true;
                // Note: openedFile remains empty
                return true;
            }
            else
                return false;
        }

        public bool GetProperty(String Name, ref String Value)
        {
            StringBuilder sbValue = new StringBuilder(MAX_STRING);
            APIRET Ret = (APIRET)PackageGetProperty(hPkg, Name, sbValue, MAX_STRING);
            if (Ret == APIRET.SUCCESS)
            {
                Value = sbValue.ToString();
                return true;
            }
            else if (Ret == APIRET.NOT_FOUND)
                return false;
            else
                return false;
        }

        public String GetProperty(String Name)
        {
            String Value = "";
            if (GetProperty(Name, ref Value))
                return (Value);
            else
                return ("");
        }

        public bool SetProperty(String Name, String Value)
        {
            APIRET Ret = (APIRET)PackageSetProperty(hPkg, Name, Value);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.FILE_CREATE_ERROR)
                return false;
            else
                return false;
        }

        public bool SetProtection(String Password, int ProtectedActions, String RequireCertificate)
        {
            if (ver == 1) return true;
            APIRET Ret = (APIRET)PackageSetProtection(hPkg, Password, ProtectedActions, RequireCertificate);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.FILE_CREATE_ERROR)
                return false;
            else
                return false;
        }

        public bool SetIcon(String FileName)
        {
            APIRET Ret = (APIRET)PackageSetIconFile(hPkg, FileName);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.NOT_FOUND)
                return false;
            else
                return false;
        }

        //
        // VirtFs functions
        private bool EnumFilesCallback(
            ref Object Data,
            [MarshalAs(UnmanagedType.LPWStr)] String FileName,
            UInt32 FileFlags,
            UInt64 CreationTime,
            UInt64 LastAccessTime,
            UInt64 LastWriteTime,
            UInt64 ChangeTime,
            UInt64 EndOfFile,
            UInt32 FileAttributes)
        {
            VirtFsNode virtFsNode = new VirtFsNode();
            virtFsNode.FileName = FileName;
            virtFsNode.FileFlags = (VIRT_FILE_FLAGS)FileFlags;
            virtFsNode.CreationTime = CreationTime;
            virtFsNode.LastAccessTime = LastAccessTime;
            virtFsNode.LastWriteTime = LastWriteTime;
            virtFsNode.ChangeTime = ChangeTime;
            virtFsNode.EndOfFile = EndOfFile;
            virtFsNode.FileAttributes = FileAttributes;
            ((List<VirtFsNode>)Data).Add(virtFsNode);
            return true;
        }

        public bool EnumFiles(
            ref List<VirtFsNode> VirtFsNodes)
        {
            VIRTFS_ENUM_CALLBACK Callback = new VIRTFS_ENUM_CALLBACK(EnumFilesCallback);
            Object Data = VirtFsNodes;
            VirtFsEnum(hPkg, Callback, ref Data);
            return true;
        }

        public bool AddFile(
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName)
        {
            VIRT_FILE_FLAGS fileFlags = VIRT_FILE_FLAGS.ISFILE | VIRT_FILE_FLAGS.DEPLOY_UPON_PRELOAD | VIRT_FILE_FLAGS.PKG_FILE;
            return AddFileEx(SrcFileName, DestFileName, bVariablizeName, fileFlags);
        }

        public bool AddFileEx(
            String SrcFileName,
            String DestFileName,
            bool bVariablizeName,
            VIRT_FILE_FLAGS fileFlags)
        {
          APIRET Ret = (APIRET)VirtFsAddEx(hPkg, SrcFileName, DestFileName, bVariablizeName, (uint)fileFlags);
          if (Ret == APIRET.SUCCESS)
            return true;
          else if (Ret == APIRET.VIRTFILES_DB_ERROR)
            return true;
          else if (Ret == APIRET.NOT_FOUND)
            return false;
          else
            return false;
        }

        public bool AddEmptyDir(
            String DirName,
            bool bVariablizeName)
        {
            APIRET Ret = (APIRET)VirtFsAddEmptyDir(hPkg, DirName, bVariablizeName);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.VIRTFILES_DB_ERROR)
                return true;
            else
                return false;
        }

        public bool AddDir(
            String SrcFolderName,
            String DestFolderName,
            bool bVariablizeName)
        {
            if (!System.IO.Directory.Exists(SrcFolderName))
                return false;

            AddEmptyDir(DestFolderName, bVariablizeName);

            string[] files = System.IO.Directory.GetFiles(SrcFolderName);
            foreach (string file in files)
            {
                if (!AddFile(file, DestFolderName + "\\" + System.IO.Path.GetFileName(file), bVariablizeName))
                    return false;
            }

            string[] subDirs = System.IO.Directory.GetDirectories(SrcFolderName);
            foreach (string dir in subDirs)
            {
                if (!AddDir(dir, DestFolderName + "\\" + System.IO.Path.GetFileName(dir), bVariablizeName))
                    return false;
            }
            return true;
        }

        public bool ExtractFile(
            String FileName,
            String TargetDir)
        {
            APIRET Ret = (APIRET)VirtFsExtract(hPkg, FileName, TargetDir);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.NOT_FOUND)
                return false;
            else if (Ret == APIRET.FILE_CREATE_ERROR)
                return false;
            else
                return false;
        }

        public bool DeleteFile(
            String FileName)
        {
            APIRET Ret = (APIRET)VirtFsDelete(hPkg, FileName);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.NOT_FOUND)
                return false;
            else
                return false;
        }

        public bool SetFileStreaming(
            String FileName)
        {
            APIRET Ret = (APIRET)VirtFsSetFileStreaming(hPkg, FileName);
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.NOT_FOUND)
                return false;
            else
                return false;
        }

        //
        // VirtReg functions
        public RegistryKey GetRegWorkKeyEx(System.Threading.AutoResetEvent abortEvent)
        {
            StringBuilder sbWorkKey = new StringBuilder(MAX_STRING);

            SafeWaitHandle waitHandle;
            if (abortEvent != null)
                waitHandle = abortEvent.SafeWaitHandle;
            else
                waitHandle = new SafeWaitHandle(IntPtr.Zero, true); ;
            APIRET Ret = (APIRET)VirtRegGetWorkKeyEx(hPkg, sbWorkKey, MAX_STRING, waitHandle);
            if (Ret == APIRET.SUCCESS)
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(sbWorkKey.ToString(), true);
                if (key == null)
                {
                    key = Registry.CurrentUser.CreateSubKey(sbWorkKey.ToString());
                }
                return (key);
            }
            else if (Ret == APIRET.INSUFFICIENT_BUFFER)
                return null;
            else
                return null;
        }

        public RegistryKey GetRegWorkKey()
        {
            return GetRegWorkKeyEx(null);
        }
        
        [DllImport("kernel32.dll")]
        static extern void OutputDebugString(string lpOutputString);

        public bool SaveRegWorkKey()
        {
            APIRET Ret = (APIRET)VirtRegSaveWorkKey(hPkg);
            OutputDebugString("SaveRegWorkKey() ret=" + (int)Ret + " LE=" + Marshal.GetLastWin32Error() + "\n");
            if (Ret == APIRET.SUCCESS)
                return true;
            else if (Ret == APIRET.INVALID_PARAMETER)
                return false;
            else
                return false;
        }

        //
        // Sandbox functions

        // Sandbox Get functions
        public UInt32 GetRegistrySandbox(String Path, bool bVariablizeName)
        {
            UInt32 SandboxFlags = 0;
            SandboxGetRegistryFlags(hPkg, Path, bVariablizeName, ref SandboxFlags);
            return SandboxFlags;
        }
        public UInt32 GetRegistrySandbox(String Path) { return GetRegistrySandbox(Path, false); }   // Overload

        public UInt32 GetFileSandbox(String Path, bool bVariablizeName)
        {
            UInt32 SandboxFlags = 0;
            SandboxGetFileFlags(hPkg, Path, bVariablizeName, ref SandboxFlags);
            return SandboxFlags;
        }
        public UInt32 GetFileSandbox(String Path) { return GetFileSandbox(Path, false); }   // Overload

        // Sandbox Set functions
        public void SetRegistrySandbox(String Path, UInt32 SandboxFlags, bool bVariablizeName)
        {
            SandboxSetRegistryFlags(hPkg, Path, bVariablizeName, SandboxFlags);
        }
        public void SetRegistrySandbox(String Path, UInt32 SandboxFlags) { SetRegistrySandbox(Path, SandboxFlags, false); }   // Overload

        public void SetFileSandbox(String Path, UInt32 SandboxFlags, bool bVariablizeName)
        {
            SandboxSetFileFlags(hPkg, Path, bVariablizeName, SandboxFlags);
        }
        public void SetFileSandbox(String Path, UInt32 SandboxFlags) { SetFileSandbox(Path, SandboxFlags, false); }   // Overload

        public void SetFileFlags(String Path, VIRT_FILE_FLAGS FileFlags)
        {
          APIRET apiRet = (APIRET)VirtFsSetFileFlags(hPkg, Path, (UInt32)FileFlags);
        }
        public VIRT_FILE_FLAGS GetFileFlags(String Path)
        {
            UInt32 FileFlags = 0;
            APIRET apiRet = (APIRET)VirtFsGetFileFlags(hPkg, Path, ref FileFlags);
            return (VIRT_FILE_FLAGS)FileFlags;
        }

        static String LPWStrToString(char[] lpwstr)
        {
            String res = "";
            for (int i = 0; i < lpwstr.Length; i += 2)
            {
                if (lpwstr[i] == 0)
                    break;
                res += lpwstr[i];
            }
            return res;
        }

        static List<UInt32> ArrayToList(UInt32[] array, UInt32 count)
        {
            List<UInt32> res = new List<UInt32>();
            for (int i = 0; i < count; i++)
                res.Add(array[i]);
            return res;
        }

        static List<VIRT_PROCESS> ArrayToList(VIRT_PROCESS[] array, UInt32 count)
        {
            List<VIRT_PROCESS> res = new List<VIRT_PROCESS>();
            for (int i = 0; i < count; i++)
                res.Add(array[i]);
            return res;
        }

        //
        // RunningApp functions
        static private void Dbg(string msg)
        {
            OutputDebugString("CameyoMenu: " + msg + "\r\n");
        }

        static private bool EnumRunningAppsCallback(
            ref Object Data,
            ref RUNNING_APP RunningAppRaw)
        {
            Dbg("EnumRunningAppsCallback: in, sizeof(RunningApp)=" + Marshal.SizeOf(RunningAppRaw));
            RunningApp runningApp = new RunningApp()
            {
                AppID = LPWStrToString(RunningAppRaw.AppID),
                CarrierExeName = LPWStrToString(RunningAppRaw.CarrierExeName),
                FriendlyName = LPWStrToString(RunningAppRaw.FriendlyName),
                CarrierPID = RunningAppRaw.CarrierPID,
                StartTickTime = RunningAppRaw.StartTickTime,
                SerialId = RunningAppRaw.SerialId,
                Processes = ArrayToList(RunningAppRaw.Processes, RunningAppRaw.TotalPIDs)
            };
            ((List<RunningApp>)Data).Add(runningApp);
            Dbg("EnumRunningAppsCallback: out");
            return true;
        }

        static public List<RunningApp> GetRunningApps()
        {
            RUNNINGAPP_ENUM_CALLBACK Callback = new RUNNINGAPP_ENUM_CALLBACK(EnumRunningAppsCallback);
            List<RunningApp> list = new List<RunningApp>();
            Object data = list;
            if ((APIRET)RunningAppEnum(Callback, ref data) == APIRET.SUCCESS)
            {
                Dbg("GetRunningApps: out");
                return list;
            }
            else
            {
                Dbg("GetRunningApps: out (null)");
                return null;
            }
        }

        static public RunningApp FindRunningApp(string appID)
        {
            List<RunningApp> list = GetRunningApps();
            if (list == null)
                return null;
            foreach (RunningApp app in list)
            {
                if (app.AppID == appID)
                    return app;
            }
            return null;
        }

        //
        // DeployedApp functions
        static private bool EnumDeployedAppsCallback(
            ref Object Data,
            [MarshalAs(UnmanagedType.LPWStr)] String AppID)
        {
            ((List<String>)Data).Add(AppID);
            return true;
        }

        static public List<String> DeployedAppIDs()
        {
            DEPLOYEDAPP_ENUM_CALLBACK Callback = new DEPLOYEDAPP_ENUM_CALLBACK(EnumDeployedAppsCallback);
            List<String> list = new List<String>();
            Object data = list;
            if ((APIRET)DeployedAppEnum(Callback, ref data) == APIRET.SUCCESS)
                return list;
            else
                return null;
        }

        static public List<DeployedApp> DeployedApps()
        {
            List<String> appIDs = DeployedAppIDs();
            List<DeployedApp> deployedApps = new List<DeployedApp>();
            foreach (String appID in appIDs)
            {
                DeployedApp deployedApp = DeployedApp.FromAppID(appID);
                if (deployedApp != null)
                    deployedApps.Add(deployedApp);
            }
            return deployedApps;
        }

        static public String DeployedAppDir(String AppID)
        {
            StringBuilder sbValue = new StringBuilder(MAX_STRING);
            APIRET Ret = (APIRET)DeployedAppGetDir(AppID, sbValue, MAX_STRING);
            if (Ret == APIRET.SUCCESS)
                return sbValue.ToString();
            else
                return null;  // Error
        }

        //
        // Helper functions
        static public String FriendlyShortcutName(String rawShortcutName)
        {
            String friendly = System.IO.Path.GetFileName(rawShortcutName);
            if (friendly.EndsWith(".lnk", StringComparison.InvariantCultureIgnoreCase))
                friendly = friendly.Substring(0, friendly.Length - 4);
            return (friendly);
        }

        public int GetIsolationMode()
        {
            // Isolation. Note: it is allowed to have no checkbox selected at all.
            string isolationModeStr = GetProperty("IsolationMode");
            if (PkgVer != 1 && !string.IsNullOrEmpty(isolationModeStr))
            {
                if (isolationModeStr.Equals("Data", StringComparison.InvariantCultureIgnoreCase))
                    return ISOLATIONMODE_DATA;
                else if (isolationModeStr.Equals("FullAccess", StringComparison.InvariantCultureIgnoreCase))
                    return ISOLATIONMODE_FULL_ACCESS;
                else if (isolationModeStr.Equals("Isolated", StringComparison.InvariantCultureIgnoreCase))
                    return ISOLATIONMODE_ISOLATED;
                else
                    return ISOLATIONMODE_CUSTOM;
            }
            else
            {
                // Legacy (before "IsolationMode" property)
                if (GetFileSandbox("") == VirtPackage.SANDBOXFLAGS_COPY_ON_WRITE &&
                    GetRegistrySandbox("") == VirtPackage.SANDBOXFLAGS_COPY_ON_WRITE &&
                    GetFileSandbox("%Personal%") == VirtPackage.SANDBOXFLAGS_PASSTHROUGH &&
                    GetFileSandbox("%Desktop%") == VirtPackage.SANDBOXFLAGS_PASSTHROUGH &&
                    GetFileSandbox("UNC") == VirtPackage.SANDBOXFLAGS_PASSTHROUGH)
                    return ISOLATIONMODE_DATA;
                else if (GetFileSandbox("") == VirtPackage.SANDBOXFLAGS_COPY_ON_WRITE &&
                    GetRegistrySandbox("") == VirtPackage.SANDBOXFLAGS_COPY_ON_WRITE)
                    return ISOLATIONMODE_ISOLATED;
                else if (GetFileSandbox("") == VirtPackage.SANDBOXFLAGS_PASSTHROUGH &&
                    GetRegistrySandbox("") == VirtPackage.SANDBOXFLAGS_PASSTHROUGH)
                    return ISOLATIONMODE_FULL_ACCESS;
                else
                    return ISOLATIONMODE_CUSTOM;
            }
        }

        public void SetIsolationMode(int IsolationMode)
        {
            if (PkgVer == 1)
            {//for 2.0.890
                uint sandboxMode = 0;
                if (IsolationMode == ISOLATIONMODE_ISOLATED || IsolationMode == ISOLATIONMODE_DATA)
                    sandboxMode = VirtPackage.SANDBOXFLAGS_COPY_ON_WRITE;
                else if (IsolationMode == ISOLATIONMODE_FULL_ACCESS)
                    sandboxMode = VirtPackage.SANDBOXFLAGS_PASSTHROUGH;
                if (sandboxMode != 0)
                {
                    SetFileSandbox("", sandboxMode);
                    SetRegistrySandbox("", sandboxMode);
                }

                // Do / undo special folders newly / previously set by Data Isolation mode
                if (IsolationMode == ISOLATIONMODE_DATA)
                {
                    SetProperty("DataMode", "TRUE");
                    SetFileSandbox("%Personal%", VirtPackage.SANDBOXFLAGS_PASSTHROUGH);
                    SetFileSandbox("%Desktop%", VirtPackage.SANDBOXFLAGS_PASSTHROUGH);
                    SetFileSandbox("UNC", VirtPackage.SANDBOXFLAGS_PASSTHROUGH);
                }
                else
                {
                    if (GetProperty("DataMode") == "TRUE")     // Need to undo special dirs changed by Data Isolation mode (as opposed to set by user)
                    {
                        SetProperty("DataMode", "FALSE");
                        SetFileSandbox("%Personal%", sandboxMode);
                        SetFileSandbox("%Desktop%", sandboxMode);
                        SetFileSandbox("UNC", sandboxMode);
                    }
                }
                return;
            }
            //For 2.6.1191
            switch (IsolationMode)
            {
                case ISOLATIONMODE_DATA:
                    SetProperty("IsolationMode", "Data");
                    break;
                case ISOLATIONMODE_ISOLATED:
                    SetProperty("IsolationMode", "Isolated");
                    break;
                case ISOLATIONMODE_FULL_ACCESS:
                    SetProperty("IsolationMode", "FullAccess");
                    break;
                case ISOLATIONMODE_CUSTOM:
                default:
                    SetProperty("IsolationMode", "Custom");
                    break;
            }
        }

        static public Hashtable ReadIniSettingsBuf(String iniBuf)
        {
            try
            {
                String[] lines = iniBuf.Split('\r', '\n');
                var values = new Hashtable(StringComparer.InvariantCultureIgnoreCase);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (String.IsNullOrEmpty(lines[i]))
                        continue;
                    try
                    {
                        int equal = lines[i].IndexOf('=');
                        if (equal != -1)
                            values.Add(lines[i].Substring(0, equal), lines[i].Substring(equal + 1));
                        else
                            values.Add(lines[i], "");
                    }
                    catch { }
                }
                return values;
            }
            catch
            {
                return null;
            }
        }

        static public Hashtable ReadIniSettings(String IniFile)
        {
            if (!File.Exists(IniFile))
                return null;
            try
            {
                String iniBuf = File.ReadAllText(IniFile, Encoding.Unicode);
                if (iniBuf.IndexOf("AppID") == -1)
                    iniBuf = File.ReadAllText(IniFile, Encoding.ASCII);  // Happens when modified with notepad
                return ReadIniSettingsBuf(iniBuf);
            }
            catch
            {
                return null;
            }
        }

        public class RunningAppsEnumerator
        {
            IntPtr Context = IntPtr.Zero;

            public List<RunningApp> GetRunningApps()
            {
                RUNNINGAPP_ENUM_CALLBACK Callback = new RUNNINGAPP_ENUM_CALLBACK(EnumRunningAppsCallback);
                List<RunningApp> list = new List<RunningApp>();
                Object data = list;
                Dbg("GetRunningApps");
                if ((APIRET)RunningAppEnumKeepAlive(ref Context, Callback, ref data) == APIRET.SUCCESS)
                    return list;
                else
                    return null;
            }

            ~RunningAppsEnumerator()
            {
                RunningAppEnumKeepAliveFree(Context);
            }
        }
    }

    public class DeployedApp
    {
        public String AppID { get { return m_AppID; } }
        internal String m_AppID;
        public String BaseDirName { get { return m_BaseDirName; } }
        internal String m_BaseDirName;
        public String CarrierExeName { get { return m_CarrierExeName; } }
        internal String m_CarrierExeName;
        public long OccupiedSize { get { return GetOccupiedSize(); } }
        public long m_OccupiedSize = -1;
        public long ExeSize { get { return GetExeSize(); } }
        public long m_ExeSize = -1;
        public String EngineVersion { get { return GetEngineVersion(); } }
        public String m_EngineVersion;

        // Basic ini settings
        public String Version { get { return GetVersion(); } }
        public String m_Version;
        public String Publisher { get { return GetPublisher(); } }
        public String m_Publisher;
        public String BuildUid { get { return GetBuildUid(); } }
        public String m_BuildUid;
        public String CloudPkgId { get { return GetCloudPkgId(); } }
        public String m_CloudPkgId;
        public String Streamer { get { return GetStreamer(); } }
        public String m_Streamer;
        public String FriendlyName { get { return GetFriendlyName(); } }
        public String m_FriendlyName;
        public String AutoLaunch { get { return GetAutoLaunch(); } }
        public String m_AutoLaunch;
        public String Shortcuts { get { return GetShortcuts(); } }
        public String m_Shortcuts;
        public String StopInheritance { get { return GetStopInheritance(); } }
        public String m_StopInheritance;
        public List<String> IntegratedComponents { get { return GetIntegratedComponents(); } }
        public List<String> m_IntegratedComponents;

        public Hashtable IniProperties { get { return m_IniProperties; } }
        internal Hashtable m_IniProperties;

        public DeployedApp(String appID, String baseDirName, String carrierExeName)
        {
            m_AppID = appID;
            m_BaseDirName = baseDirName;
            m_CarrierExeName = carrierExeName;
            m_IniProperties = VirtPackage.QuickReadIniValues(carrierExeName); //ReadIniSettings(Path.Combine(baseDirName, "VirtApp.ini"));
        }

        public static long RecursiveDirSize(DirectoryInfo d) 
        {    
            long Size = 0;    
            
            // Add file sizes.
            try
            {
                FileInfo[] fis = d.GetFiles();
                foreach (FileInfo fi in fis)
                {
                    try
                    {
                        Size += fi.Length;
                    }
                    catch { }
                }
            }
            catch { }

            // Add subdirectory sizes.
            try
            {
                DirectoryInfo[] dis = d.GetDirectories();
                foreach (DirectoryInfo di in dis)
                {
                    Size += RecursiveDirSize(di);
                }
            }
            catch { }
            return(Size);  
        }

        private String GetVersion()
        {
            if (!String.IsNullOrEmpty(m_Version) || m_IniProperties == null || m_IniProperties["Version"] == null)
                return m_Version;
            m_Version = (String)IniProperties["Version"];
            return m_Version;
        }

        private String GetPublisher()
        {
            if (!String.IsNullOrEmpty(m_Publisher) || m_IniProperties == null || m_IniProperties["Publisher"] == null)
                return m_Publisher;
            m_Publisher = (String)IniProperties["Publisher"];
            return m_Publisher;
        }

        private String GetBuildUid()
        {
            if (!String.IsNullOrEmpty(m_BuildUid) || m_IniProperties == null || m_IniProperties["BuildUID"] == null)
                return m_BuildUid;
            m_BuildUid = (String)IniProperties["BuildUID"];
            return m_BuildUid;
        }

        private String GetCloudPkgId()
        {
            if (!String.IsNullOrEmpty(m_CloudPkgId) || m_IniProperties == null || m_IniProperties["CloudPkgId"] == null)
                return m_CloudPkgId;
            m_CloudPkgId = (String)IniProperties["CloudPkgId"];
            return m_CloudPkgId;
        }

        private String GetStreamer()
        {
            if (!String.IsNullOrEmpty(m_Streamer) || m_IniProperties == null || m_IniProperties["Streamer"] == null)
                return m_Streamer;
            m_Streamer = (String)IniProperties["Streamer"];
            return m_Streamer;
        }

        private String GetFriendlyName()
        {
            if (!String.IsNullOrEmpty(m_FriendlyName) || m_IniProperties == null || m_IniProperties["FriendlyName"] == null)
                return m_FriendlyName;
            m_FriendlyName = (String)IniProperties["FriendlyName"];
            return m_FriendlyName;
        }

        private String GetAutoLaunch()
        {
            if (!String.IsNullOrEmpty(m_AutoLaunch) || m_IniProperties == null || m_IniProperties["AutoLaunch"] == null)
                return m_AutoLaunch;
            m_AutoLaunch = (String)IniProperties["AutoLaunch"];
            return m_AutoLaunch;
        }

        private String GetShortcuts()
        {
            if (!String.IsNullOrEmpty(m_Shortcuts) || m_IniProperties == null || m_IniProperties["Shortcuts"] == null)
                return m_Shortcuts;
            m_Shortcuts = (String)IniProperties["Shortcuts"];
            return m_Shortcuts;
        }

        private String GetStopInheritance()
        {
            if (!String.IsNullOrEmpty(m_StopInheritance) || m_IniProperties == null || m_IniProperties["StopInheritance"] == null)
                return m_StopInheritance;
            m_StopInheritance = (String)IniProperties["StopInheritance"];
            return m_StopInheritance;
        }

        // Returns a list of integrated components, or null if not integrated
        private List<String> GetIntegratedComponents()
        {
            if (m_IntegratedComponents != null)   // Result already cached?
                return (m_IntegratedComponents.Count == 0 ? null : m_IntegratedComponents);

            // Not cached yet (first time). Perform.
            m_IntegratedComponents = new List<string>();   // Leaving it empty if no integration, so as to indicate: 'cached'
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\VOS\\" + this.AppID + "\\Integrated", false);
                if (key != null)
                {
                    foreach (string keyName in key.GetSubKeyNames())
                        m_IntegratedComponents.Add(keyName);
                }
                key.Close();
            }
            catch { }
            return (m_IntegratedComponents.Count == 0 ? null : m_IntegratedComponents);
        }

        public static long GetOccuppiedAppSize(string BaseDirName)
        {
            if (!Directory.Exists(BaseDirName))
            {
                return 0;
            }
            DirectoryInfo d = new DirectoryInfo(BaseDirName);
            return RecursiveDirSize(d);
        }

        private long GetOccupiedSize()
        {
            if (m_OccupiedSize != -1)
                return m_OccupiedSize;
            m_OccupiedSize = GetOccuppiedAppSize(m_BaseDirName);
            return m_OccupiedSize;
        }

        private long GetExeSize()
        {
            if (m_ExeSize != -1)
                return m_ExeSize;
            if (!File.Exists(m_CarrierExeName))
            {
                m_ExeSize = 0;
                return m_ExeSize;
            }
            FileInfo f = new FileInfo(m_CarrierExeName);
            m_ExeSize = f.Length;
            return m_ExeSize;
        }

        private String GetEngineVersion()
        {
            if (!String.IsNullOrEmpty(m_EngineVersion))
                return m_EngineVersion;

            // EngineVersion property (available only on 2.0.713 and higher)
            m_EngineVersion = (String)IniProperties["EngineVersion"];
            if (!String.IsNullOrEmpty(m_EngineVersion))
                return m_EngineVersion;

            // No property (backward compatibility mode); try to find AppVirtDll
            String appVirtDll = m_BaseDirName + "\\AppVirtDll_" + m_AppID + ".dll";
            if (!File.Exists(appVirtDll))
                return "";
            System.Diagnostics.FileVersionInfo fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(appVirtDll);
            if (fileVersionInfo == null)
                return "";
            m_EngineVersion = fileVersionInfo.FileVersion.Replace(" ", "").Replace(",", ".");  // virtPkg.EngineVer is in the form: "1, 7, 534, 0"
            return m_EngineVersion;
        }

        delegate bool DirectoryExistsDelegate(string folder);
        static bool DirectoryExistsTimeout(string path, int millisecondsTimeout)
        {
            try
            {
                DirectoryExistsDelegate callback = new DirectoryExistsDelegate(Directory.Exists);
                IAsyncResult result = callback.BeginInvoke(path, null, null);
                if (result.AsyncWaitHandle.WaitOne(millisecondsTimeout, false))
                {
                    return callback.EndInvoke(result);
                }
                else
                {
                    //callback.EndInvoke(result);   // Problem: this seems to make the current thread block for response!
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        delegate bool FileExistsDelegate(string file);
        static bool FileExistsTimeout(string path, int millisecondsTimeout)
        {
            try
            {
                FileExistsDelegate callback = new FileExistsDelegate(File.Exists);
                IAsyncResult result = callback.BeginInvoke(path, null, null);
                if (result.AsyncWaitHandle.WaitOne(millisecondsTimeout, false))
                {
                    return callback.EndInvoke(result);
                }
                else
                {
                    //callback.EndInvoke(result);   // Problem: this seems to make the current thread block for response!
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static public DeployedApp FromAppID(String appID)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\VOS\\" + appID, false);
                if (key == null)
                    return null;
                String baseDirName = (String)key.GetValue("BaseDirName");
                String carrierExeName = (String)key.GetValue("CarrierExeName");
                System.Diagnostics.Debug.WriteLine(appID + ": " + carrierExeName);

                // Detect & avoid disconnected shares / mapped drives, as their I/O can take a long time before failing..
                if (!FileExistsTimeout(carrierExeName, 1000)) //|| !DirectoryExistsTimeout(baseDirName, 1000))
                    return null;

                return new DeployedApp(appID, baseDirName, carrierExeName);
            }
            catch
            {
                return null;
            }
        }
    }

    //
    // Packager command line functions
    public class PackagerCmdLine
    {
        static public Hashtable ReadIni(string appExe, string packagerExe)
        {
            string tempFile = Path.GetTempFileName();
            try { System.IO.File.Delete(tempFile); }
            catch { }

            int exitCode = -1;
            string args = string.Format("-Quiet -ExtractIni \"{0}\" \"{1}\"", appExe, tempFile);
            bool execOk = ExecProg(packagerExe, args, true, ref exitCode);
            if (!execOk)
                return null;
            if (!File.Exists(tempFile))
                return null;
            try   // finally
            {
                if (exitCode != (int)VirtPackageAPI.VirtPackage.APIRET.SUCCESS)
                    return null;
                return VirtPackage.ReadIniSettings(tempFile);
            }
            finally
            {
                try { System.IO.File.Delete(tempFile); }
                catch { }
            }
        }

        static public bool SetProperties(string appExe, Hashtable values, string packagerExe)
        {
            // -SetProperties
            int exitCode = -1;
            string properties = "";
            foreach (DictionaryEntry value in values)
            {
                if (!string.IsNullOrEmpty(properties))
                    properties += ",,";
                properties += value.Key + "=" + value.Value;
            }
            string args = string.Format("-Quiet \"-SetProperties:{0}\" {1}", properties, appExe);
            bool execOk = ExecProg(packagerExe, args, true, ref exitCode);
            return (execOk && exitCode == (int)VirtPackageAPI.VirtPackage.APIRET.SUCCESS);
        }

        static public string PackagerExe()
        {
            return Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "Packager.exe");
        }

        static public bool ExecProg(String fileName, String args, bool wait, ref int exitCode)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo(fileName, args);
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                procStartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                procStartInfo.CreateNoWindow = true;
                procStartInfo.UseShellExecute = false;
                proc.StartInfo = procStartInfo;
                proc.Start();
                if (wait)
                {
                    proc.WaitForExit();
                    exitCode = proc.ExitCode;
                }
                return true;
            }
            catch { }
            return false;
        }
    }
}
