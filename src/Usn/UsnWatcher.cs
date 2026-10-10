using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Arama.App;
using Arama.Index;
using Microsoft.Win32.SafeHandles;

namespace Arama.Usn
{
    public struct JournalState
    {
        public long JournalId;
        public long FirstUsn;
        public long NextUsn;
    }

    public static class UsnJournal
    {
        /// <summary>Journal bilgisini alır; kapalıysa açar (32 MB, 8 MB artış).</summary>
        public static JournalState QueryOrCreate(SafeFileHandle volume)
        {
            JournalState state;
            int error = Query(volume, out state);
            if (error == Native.ERROR_JOURNAL_NOT_ACTIVE)
            {
                var create = new byte[16];
                BitConverter.GetBytes(32L * 1024 * 1024).CopyTo(create, 0);
                BitConverter.GetBytes(8L * 1024 * 1024).CopyTo(create, 8);
                int returned;
                if (!Native.DeviceIoControl(volume, Native.FSCTL_CREATE_USN_JOURNAL, create, create.Length, null, 0, out returned, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "USN journal açılamadı");
                error = Query(volume, out state);
            }
            if (error != 0) throw new Win32Exception(error, "USN journal okunamadı");
            return state;
        }

        /// <summary>0: başarı; aksi halde Win32 hata kodu.</summary>
        public static int Query(SafeFileHandle volume, out JournalState state)
        {
            state = new JournalState();
            var output = new byte[128];
            int returned;
            if (!Native.DeviceIoControl(volume, Native.FSCTL_QUERY_USN_JOURNAL, null, 0, output, output.Length, out returned, IntPtr.Zero))
                return Marshal.GetLastWin32Error();
            state.JournalId = BitConverter.ToInt64(output, 0);
            state.FirstUsn = BitConverter.ToInt64(output, 8);
            state.NextUsn = BitConverter.ToInt64(output, 16);
            return 0;
        }
    }

    public struct FileInfoData
    {
        public long Size;
        public long Modified;
        public uint Attributes;
    }

    public static class FileInfoReader
    {
        /// <summary>Dosyayı numarasıyla açıp boyut, değiştirilme zamanı ve özniteliklerini okur.</summary>
        public static bool TryRead(SafeFileHandle volume, long fileReference, out FileInfoData data)
        {
            data = new FileInfoData();
            var descriptor = new Native.FILE_ID_DESCRIPTOR();
            descriptor.Size = 24;
            descriptor.Type = 0;
            descriptor.FileId = fileReference;
            using (var handle = Native.OpenFileById(volume, ref descriptor, Native.FILE_READ_ATTRIBUTES,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE, IntPtr.Zero, Native.FILE_FLAG_BACKUP_SEMANTICS))
            {
                if (handle.IsInvalid) return false;
                Native.BY_HANDLE_FILE_INFORMATION info;
                if (!Native.GetFileInformationByHandle(handle, out info)) return false;
                data.Size = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
                data.Modified = ((long)info.LastWriteTimeHigh << 32) | info.LastWriteTimeLow;
                data.Attributes = info.FileAttributes;
                return true;
            }
        }
    }

    /// <summary>Bir diskin USN journal'ını 500 ms aralıkla okuyup değişiklikleri indekse uygular.</summary>
    public sealed class UsnWatcher : IDisposable
    {
        const int BufferSize = 64 * 1024;
        readonly SafeFileHandle volume;
        readonly FileIndex index;
        readonly long journalId;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        long nextUsn;
        Thread thread;

        /// <summary>Bir grup değişiklik uygulandıktan sonra, değişen kayıt numaralarıyla (arka plan iş parçacığında).</summary>
        public event Action<ICollection<int>> Changed;

        /// <summary>Journal taştı/sıfırlandı: indeks yeniden kurulmalı. Sonrasında izleyici durur.</summary>
        public event Action ResetRequired;

        public UsnWatcher(SafeFileHandle volume, FileIndex index, long journalId, long startUsn)
        {
            this.volume = volume;
            this.index = index;
            this.journalId = journalId;
            nextUsn = startUsn;
        }

        public void Start()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "UsnWatcher " + index.Drive;
            thread.Start();
        }

        public void Dispose()
        {
            stop.Set();
            if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
        }

        void Run()
        {
            var input = new byte[40];
            var output = new byte[BufferSize];
            var events = new List<UsnEvent>();
            try
            {
                while (!stop.WaitOne(0))
                {
                    // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask, ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID
                    BitConverter.GetBytes(nextUsn).CopyTo(input, 0);
                    BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(input, 8);
                    BitConverter.GetBytes(0u).CopyTo(input, 12);
                    BitConverter.GetBytes(0L).CopyTo(input, 16);
                    BitConverter.GetBytes(0L).CopyTo(input, 24);
                    BitConverter.GetBytes(journalId).CopyTo(input, 32);
                    int returned;
                    if (!Native.DeviceIoControl(volume, Native.FSCTL_READ_USN_JOURNAL, input, input.Length, output, output.Length, out returned, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (IsResetError(error) || JournalChanged())
                        {
                            Raise(ResetRequired);
                            return;
                        }
                        ErrorLog.Write("USN okuma hatası " + index.Drive + ": " + error);
                        if (stop.WaitOne(2000)) return;
                        continue;
                    }
                    events.Clear();
                    long newNext = UsnRecordParser.Parse(output, returned, events);
                    if (events.Count == 0)
                    {
                        nextUsn = newNext;
                        if (stop.WaitOne(500)) return;
                        continue;
                    }
                    Apply(events);
                    nextUsn = newNext;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        void Apply(List<UsnEvent> events)
        {
            Dictionary<int, long> refresh;
            index.Lock.EnterWriteLock();
            try { refresh = UsnApplier.Apply(index, events); }
            finally { index.Lock.ExitWriteLock(); }

            if (refresh.Count > 0)
            {
                var infos = new List<KeyValuePair<int, FileInfoData>>();
                foreach (var pair in refresh)
                {
                    FileInfoData data;
                    if (FileInfoReader.TryRead(volume, pair.Value, out data)) infos.Add(new KeyValuePair<int, FileInfoData>(pair.Key, data));
                }
                index.Lock.EnterWriteLock();
                try
                {
                    foreach (var info in infos) index.SetInfo(info.Key, info.Value.Size, info.Value.Modified, info.Value.Attributes);
                }
                finally { index.Lock.ExitWriteLock(); }
            }

            if (index.TailCount > FileIndex.TailRebuildThreshold)
                ThreadPool.QueueUserWorkItem(_ => { if (index.BuildSortedOrder()) MemoryTrim.Now(); });
            var changedIds = new List<int>(events.Count);
            foreach (var ev in events)
                if (ev.Id >= FileIndex.FirstUserRecord) changedIds.Add(ev.Id);
            var changedHandler = Changed;
            if (changedHandler != null) changedHandler(changedIds);
        }

        static bool IsResetError(int error)
        {
            return error == Native.ERROR_JOURNAL_ENTRY_DELETED || error == Native.ERROR_JOURNAL_DELETE_IN_PROGRESS || error == Native.ERROR_JOURNAL_NOT_ACTIVE;
        }

        bool JournalChanged()
        {
            JournalState state;
            return UsnJournal.Query(volume, out state) != 0 || state.JournalId != journalId || nextUsn < state.FirstUsn;
        }

        static void Raise(Action handler)
        {
            if (handler != null) handler();
        }
    }
}
