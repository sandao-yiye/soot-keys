using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SootKeys
{
    // Win10/11 ship WinSQLite. Each poll reads one committed snapshot using
    // the same primary-key lookups as the settings store; WAL and rollback
    // journals are both handled by SQLite.
    internal static class CodexStateDatabase
    {
        internal enum Result { Ready, Busy, Unavailable }
        const int Ok = 0, Busy = 5, Locked = 6, Row = 100, Done = 101;
        const string Visibility = " FROM settings WHERE scope='global' AND key='electron-avatar-overlay-open'";
        const string OwnerScope = "(SELECT 'electron-owner-atoms:' || trim(value, '\"') FROM settings " +
            "WHERE scope='global' AND key='electron-renderer-content-owner' AND value <> 'null')";
        const string CloudQuery = "SELECT value, CASE WHEN value='true' THEN (SELECT value FROM settings WHERE " +
            "key='migrated-cloud-pet-ids-v1' AND scope=coalesce(" + OwnerScope + ", 'atom')) END" + Visibility;
        static readonly byte[] LocalQueryBytes = Utf8("SELECT value, NULL" + Visibility);
        static readonly byte[] CloudQueryBytes = Utf8(CloudQuery);

        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_busy_timeout(IntPtr db, int millis);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_column_text16(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
        static extern int sqlite3_close(IntPtr db);

        static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + '\0'); }
        static Result Failure(int code) { return code==Busy || code==Locked ? Result.Busy : Result.Unavailable; }

        internal static Result Read(string file, bool local, out bool open, out string migrations)
        {
            open=false;
            migrations=null;
            IntPtr db=IntPtr.Zero, statement=IntPtr.Zero;
            try
            {
                const int ReadOnly = 1;
                int rc=sqlite3_open_v2(Utf8(file),out db,ReadOnly,IntPtr.Zero);
                if (rc!=Ok) return Failure(rc);
                sqlite3_busy_timeout(db,25);
                rc=sqlite3_prepare_v2(db,local ? LocalQueryBytes : CloudQueryBytes,-1,out statement,IntPtr.Zero);
                if (rc!=Ok) return Failure(rc);
                rc=sqlite3_step(statement);
                if (rc==Done) return Result.Ready;
                if (rc!=Row) return Failure(rc);
                open=Marshal.PtrToStringUni(sqlite3_column_text16(statement,0))=="true";
                migrations=Marshal.PtrToStringUni(sqlite3_column_text16(statement,1));
                return Result.Ready;
            }
            finally
            {
                if (statement!=IntPtr.Zero) sqlite3_finalize(statement);
                if (db!=IntPtr.Zero) sqlite3_close(db);
            }
        }
    }
}
