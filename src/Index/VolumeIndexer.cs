using System;
using System.Collections.Generic;
using System.Threading;
using Arama.App;
using Arama.Ntfs;
using Arama.Usn;
using Microsoft.Win32.SafeHandles;

namespace Arama.Index
{
    /// <summary>
    /// Bir disk için: journal konumunu not al → flush → MFT tara → sırala → canlı takibe geç.
    /// Journal sıfırlanırsa arka planda yeniden tarar; bu sırada eski indeks kullanılır.
    /// </summary>
    public sealed class VolumeIndexer : IDisposable
    {
        public readonly char Drive;
        readonly object sync = new object();
        volatile FileIndex index;
        volatile string status = "bekliyor";
        volatile int corruptRecords;
        volatile bool disposed;
        SafeFileHandle volume;
        UsnWatcher watcher;

        /// <summary>Arayüzün henüz işlemediği değişen kayıtlar (artımlı sonuç güncellemesi için).</summary>
        public readonly ChangeSet Changes = new ChangeSet(100000);

        /// <summary>İndeks hazır olduğunda ve her değişiklik grubundan sonra (arka plan iş parçacığında).</summary>
        public event Action IndexChanged;

        public VolumeIndexer(char drive)
        {
            Drive = char.ToUpperInvariant(drive);
        }

        public FileIndex Index { get { return index; } }
        public string Status { get { return status; } }
        public int CorruptRecords { get { return corruptRecords; } }

        public void Start()
        {
            var thread = new Thread(Build);
            thread.IsBackground = true;
            thread.Name = "Indexer " + Drive;
            thread.Start();
        }

        void Build()
        {
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    if (volume == null) volume = MftReader.OpenVolume(Drive);
                }
                var journal = new JournalState();
                bool live = true;
                try { journal = UsnJournal.QueryOrCreate(volume); }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                    live = false;
                }
                MftReader.FlushVolume(Drive);
                status = "indeksleniyor… %0";
                MftScanResult result = MftReader.Read(volume, Drive, p => status = "indeksleniyor… %" + p);
                status = "sıralanıyor…";
                result.Index.BuildSortedOrder();
                lock (sync)
                {
                    if (disposed) return;
                    corruptRecords = result.CorruptRecords;
                    index = result.Index;
                    if (watcher != null) watcher.Dispose();
                    watcher = null;
                    if (live)
                    {
                        watcher = new UsnWatcher(volume, index, journal.JournalId, journal.NextUsn);
                        watcher.Changed += OnWatcherChanged;
                        watcher.ResetRequired += OnResetRequired;
                        watcher.Start();
                        status = "hazır";
                    }
                    else status = "hazır (canlı güncelleme yok)";
                }
                Changes.RequireFullSearch();
                MemoryTrim.Now();
                RaiseIndexChanged();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                status = "hata: " + ex.Message;
            }
        }

        void OnResetRequired()
        {
            status = "journal sıfırlandı, yeniden taranıyor…";
            Start();
        }

        void OnWatcherChanged(ICollection<int> changedIds)
        {
            Changes.Add(changedIds);
            RaiseIndexChanged();
        }

        void RaiseIndexChanged()
        {
            var handler = IndexChanged;
            if (handler != null) handler();
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                if (watcher != null) watcher.Dispose();
                if (volume != null) volume.Dispose();
            }
        }
    }
}
