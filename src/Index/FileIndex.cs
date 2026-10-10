using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Arama.Search;

namespace Arama.Index
{
    /// <summary>
    /// Bir NTFS diskinin bellek içi indeksi. Girdi numarası = MFT kayıt numarası.
    /// Load* metotları yalnızca tarama sırasında (indeks paylaşılmadan) kullanılır.
    /// Diğer metotlarda okuma/yazma kilidi (Lock) çağıranın sorumluluğundadır;
    /// BuildSortedOrder kilitleri kendisi alır.
    /// </summary>
    public sealed class FileIndex
    {
        public const int RootId = 5;
        public const int FirstUserRecord = 24;
        public const int TailRebuildThreshold = 10000;
        /// <summary>Klasör yolu önbelleğinin en fazla girdi sayısı (aşılınca temizlenir; bellek sınırı).</summary>
        public const int MaxCachedDirectories = 65536;
        public const byte FlagLive = 1, FlagDirectory = 2, FlagHidden = 4, FlagSystem = 8, FlagInTail = 16;
        const uint AttrHidden = 0x2, AttrSystem = 0x4, AttrDirectory = 0x10;

        public readonly char Drive;
        public readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

        internal char[] names;
        internal int[] nameOffset;
        internal ushort[] nameLength;
        internal int[] parent;
        internal long[] size;
        internal long[] mtime;
        internal byte[] flags;
        int[] modSeq;
        int namesUsed;
        int capacity;
        int liveCount;
        int seq;
        bool trackTail;
        int rebuilding;
        SortedView sorted;
        readonly List<int> tail = new List<int>();
        readonly ConcurrentDictionary<int, string> dirPathCache = new ConcurrentDictionary<int, string>();
        int cachedDirs;

        public FileIndex(char drive, int initialCapacity)
        {
            Drive = char.ToUpperInvariant(drive);
            capacity = Math.Max(initialCapacity, 64);
            names = new char[Math.Max(1 << 16, (int)Math.Min((long)capacity * 12, 1 << 28))];
            nameOffset = new int[capacity];
            nameLength = new ushort[capacity];
            parent = new int[capacity];
            size = new long[capacity];
            mtime = new long[capacity];
            flags = new byte[capacity];
            modSeq = new int[capacity];
        }

        public int Capacity { get { return capacity; } }
        public int LiveCount { get { return liveCount; } }
        public int TailCount { get { return tail.Count; } }
        public bool HasSortedOrder { get { return sorted != null; } }
        internal int[] SortedByName { get { return sorted == null ? null : sorted.Ids; } }
        internal SortedView Sorted { get { return sorted; } }
        internal int DirCacheCount { get { return dirPathCache.Count; } }

        internal int[] TailSnapshot()
        {
            return tail.ToArray();
        }

        // ---- Tarama sırasında yükleme ----

        public void LoadRecord(int id, bool isDirectory, long modified, uint attributes)
        {
            EnsureCapacity(id);
            flags[id] = (byte)(FlagLive | AttrFlags(isDirectory, attributes));
            mtime[id] = modified;
        }

        public void LoadName(int id, string name, int parentId)
        {
            EnsureCapacity(id);
            WriteName(id, name);
            parent[id] = parentId;
        }

        public void LoadSize(int id, long value)
        {
            EnsureCapacity(id);
            size[id] = value;
        }

        /// <summary>
        /// Meta dosyaları, $Extend altını, ismi olmayanları ve köke bağlanmayanları eler;
        /// isim tamponunu kullanılan boyuta (küçük bir payla) kırpar.
        /// </summary>
        public void FinishLoad()
        {
            var state = new byte[capacity];
            var stack = new List<int>();
            liveCount = 0;
            for (int id = 0; id < capacity; id++)
            {
                if ((flags[id] & FlagLive) == 0) continue;
                if (Resolve(id, state, stack) == 1) liveCount++;
                else flags[id] = 0;
            }
            int trimmed = namesUsed + Math.Max(256, namesUsed / 16);
            if (trimmed < names.Length) Array.Resize(ref names, trimmed);
        }

        // 1: köke bağlı, 2: geçersiz, 3: ziyaret ediliyor (döngü tespiti)
        byte Resolve(int id, byte[] state, List<int> stack)
        {
            stack.Clear();
            int cur = id;
            byte result;
            while (true)
            {
                if (cur == RootId) { result = 1; break; }
                if (cur < FirstUserRecord || cur >= capacity || (flags[cur] & FlagLive) == 0 || nameLength[cur] == 0) { result = 2; break; }
                byte s = state[cur];
                if (s == 1 || s == 2) { result = s; break; }
                if (s == 3) { result = 2; break; }
                state[cur] = 3;
                stack.Add(cur);
                cur = parent[cur];
            }
            foreach (int x in stack) state[x] = result;
            return result;
        }

        // ---- Canlı güncelleme (yazma kilidi altında) ----

        /// <summary>Oluşturma / yeniden adlandırma / taşıma. true: girdi yeni eklendi.</summary>
        public bool Upsert(int id, string name, int parentId, uint attributes)
        {
            EnsureCapacity(id);
            bool wasLive = (flags[id] & FlagLive) != 0;
            bool isDirectory = (attributes & AttrDirectory) != 0;
            bool changed = !wasLive || parent[id] != parentId || !NameEquals(id, name);
            if (changed)
            {
                if (wasLive && (flags[id] & FlagDirectory) != 0) ClearDirCache();
                WriteName(id, name);
                parent[id] = parentId;
                MarkModified(id);
            }
            if (!wasLive)
            {
                size[id] = 0;
                mtime[id] = 0;
                liveCount++;
            }
            flags[id] = (byte)(FlagLive | AttrFlags(isDirectory, attributes) | (flags[id] & FlagInTail));
            return !wasLive;
        }

        public void Remove(int id)
        {
            if (id < 0 || id >= capacity || (flags[id] & FlagLive) == 0) return;
            if ((flags[id] & FlagDirectory) != 0) ClearDirCache();
            flags[id] = (byte)(flags[id] & FlagInTail);
            modSeq[id] = ++seq;
            liveCount--;
        }

        public void SetInfo(int id, long fileSize, long modified, uint attributes)
        {
            if (id < 0 || id >= capacity || (flags[id] & FlagLive) == 0) return;
            bool isDirectory = (attributes & AttrDirectory) != 0;
            size[id] = isDirectory ? 0 : fileSize;
            mtime[id] = modified;
            flags[id] = (byte)(FlagLive | AttrFlags(isDirectory, attributes) | (flags[id] & FlagInTail));
        }

        void MarkModified(int id)
        {
            modSeq[id] = ++seq;
            if (trackTail && (flags[id] & FlagInTail) == 0)
            {
                flags[id] |= FlagInTail;
                tail.Add(id);
            }
        }

        // ---- Okuma (okuma kilidi altında) ----

        public bool IsLive(int id)
        {
            return id >= 0 && id < capacity && (flags[id] & FlagLive) != 0;
        }

        public bool IsDirectory(int id) { return (flags[id] & FlagDirectory) != 0; }
        public int GetParent(int id) { return parent[id]; }
        public long GetSize(int id) { return size[id]; }
        public long GetModified(int id) { return mtime[id]; }

        public string GetName(int id)
        {
            return new string(names, nameOffset[id], nameLength[id]);
        }

        /// <summary>Girdiyi içeren klasörün yolu ("C:\Users"); kökteki girdi için "C:\".</summary>
        public string GetDirectoryPath(int id)
        {
            return DirPath(parent[id]);
        }

        public string GetFullPath(int id)
        {
            return Combine(DirPath(parent[id]), GetName(id));
        }

        string DirPath(int dirId)
        {
            if (dirId == RootId) return Drive + ":\\";
            string cached;
            if (dirPathCache.TryGetValue(dirId, out cached)) return cached;
            var chain = new List<int>();
            int cur = dirId;
            string basePath;
            bool orphan = false;
            while (true)
            {
                if (cur == RootId) { basePath = Drive + ":\\"; break; }
                if (dirPathCache.TryGetValue(cur, out cached)) { basePath = cached; break; }
                if (cur < 0 || cur >= capacity || nameLength[cur] == 0 || chain.Count > 1024)
                {
                    basePath = Drive + ":\\?";
                    orphan = true;
                    break;
                }
                chain.Add(cur);
                cur = parent[cur];
            }
            string path = basePath;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                path = Combine(path, GetName(chain[i]));
                if (!orphan) CacheDir(chain[i], path);
            }
            return path;
        }

        void CacheDir(int dirId, string path)
        {
            if (!dirPathCache.TryAdd(dirId, path)) return;
            if (Interlocked.Increment(ref cachedDirs) > MaxCachedDirectories) ClearDirCache();
        }

        void ClearDirCache()
        {
            dirPathCache.Clear();
            Interlocked.Exchange(ref cachedDirs, 0);
        }

        static string Combine(string dir, string name)
        {
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        // ---- Sıralama ----

        /// <summary>
        /// Ada göre sıralı diziyi kurar. Sıralama kilit dışında, anlık görüntü üzerinde yapılır;
        /// görüntüden sonra değişen girdiler kuyrukta kalır. Zaten çalışıyorsa false döner.
        /// </summary>
        public bool BuildSortedOrder()
        {
            if (Interlocked.CompareExchange(ref rebuilding, 1, 0) != 0) return false;
            try
            {
                int[] ids, offsets;
                ushort[] lengths;
                char[] buffer;
                int snapshotSeq;
                Lock.EnterReadLock();
                try
                {
                    trackTail = true;
                    ids = new int[liveCount];
                    int n = 0;
                    for (int id = 0; id < capacity && n < ids.Length; id++)
                        if ((flags[id] & FlagLive) != 0) ids[n++] = id;
                    if (n != ids.Length) Array.Resize(ref ids, n);
                    offsets = (int[])nameOffset.Clone();
                    lengths = (ushort[])nameLength.Clone();
                    buffer = names;
                    snapshotSeq = seq;
                }
                finally { Lock.ExitReadLock(); }

                Array.Sort(ids, (x, y) => CompareNameChars(buffer, offsets[x], lengths[x], buffer, offsets[y], lengths[y]));

                Lock.EnterWriteLock();
                try
                {
                    var keep = new List<int>();
                    foreach (int id in tail)
                    {
                        if (modSeq[id] > snapshotSeq) keep.Add(id);
                        else flags[id] = (byte)(flags[id] & ~FlagInTail);
                    }
                    tail.Clear();
                    tail.AddRange(keep);
                    sorted = RelayoutNames(ids);
                }
                finally { Lock.ExitWriteLock(); }
                return true;
            }
            finally { Interlocked.Exchange(ref rebuilding, 0); }
        }

        /// <summary>
        /// İsim tamponunu ada göre sıralı düzende yeniden yazar (yazma kilidi altında): sıralı tarama belleği
        /// art arda okur, rastgele erişim ve önbellek ıskalaması olmaz. Boşa düşmüş eski isim alanları da atılır.
        /// </summary>
        SortedView RelayoutNames(int[] ids)
        {
            long total = 0;
            for (int id = 0; id < capacity; id++) total += nameLength[id];
            var buffer = new char[total + Math.Max(256, total / 16)];
            var offsets = new int[ids.Length + 1];
            var copied = new bool[capacity];
            int pos = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                offsets[i] = pos;
                pos = CopyName(id, buffer, pos);
                copied[id] = true;
            }
            offsets[ids.Length] = pos;
            for (int id = 0; id < capacity; id++)
                if (!copied[id] && nameLength[id] > 0) pos = CopyName(id, buffer, pos);
            names = buffer;
            namesUsed = pos;
            return new SortedView(ids, offsets, buffer);
        }

        int CopyName(int id, char[] buffer, int pos)
        {
            Array.Copy(names, nameOffset[id], buffer, pos, nameLength[id]);
            nameOffset[id] = pos;
            return pos + nameLength[id];
        }

        /// <summary>Katlanmış isim karşılaştırması; eşitse sıralı (ordinal) karşılaştırma.</summary>
        public static int CompareNames(FileIndex a, int ia, FileIndex b, int ib)
        {
            return CompareNameChars(a.names, a.nameOffset[ia], a.nameLength[ia], b.names, b.nameOffset[ib], b.nameLength[ib]);
        }

        internal static int CompareNameChars(char[] na, int oa, int la, char[] nb, int ob, int lb)
        {
            int n = Math.Min(la, lb);
            for (int i = 0; i < n; i++)
            {
                char x = TextFold.Fold(na[oa + i]), y = TextFold.Fold(nb[ob + i]);
                if (x != y) return x - y;
            }
            if (la != lb) return la - lb;
            for (int i = 0; i < n; i++)
            {
                char x = na[oa + i], y = nb[ob + i];
                if (x != y) return x - y;
            }
            return 0;
        }

        // ---- Yardımcılar ----

        static int AttrFlags(bool isDirectory, uint attributes)
        {
            int f = 0;
            if (isDirectory) f |= FlagDirectory;
            if ((attributes & AttrHidden) != 0) f |= FlagHidden;
            if ((attributes & AttrSystem) != 0) f |= FlagSystem;
            return f;
        }

        bool NameEquals(int id, string name)
        {
            if (nameLength[id] != name.Length) return false;
            int offset = nameOffset[id];
            for (int i = 0; i < name.Length; i++)
                if (names[offset + i] != name[i]) return false;
            return true;
        }

        void WriteName(int id, string name)
        {
            if (namesUsed + name.Length > names.Length)
                Array.Resize(ref names, Math.Max(names.Length * 2, namesUsed + name.Length));
            name.CopyTo(0, names, namesUsed, name.Length);
            nameOffset[id] = namesUsed;
            nameLength[id] = (ushort)name.Length;
            namesUsed += name.Length;
        }

        void EnsureCapacity(int id)
        {
            if (id < capacity) return;
            int newCapacity = Math.Max(id + 1, capacity + capacity / 2);
            Array.Resize(ref nameOffset, newCapacity);
            Array.Resize(ref nameLength, newCapacity);
            Array.Resize(ref parent, newCapacity);
            Array.Resize(ref size, newCapacity);
            Array.Resize(ref mtime, newCapacity);
            Array.Resize(ref flags, newCapacity);
            Array.Resize(ref modSeq, newCapacity);
            capacity = newCapacity;
        }
    }
}
