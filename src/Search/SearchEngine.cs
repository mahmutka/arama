using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arama.Index;

namespace Arama.Search
{
    public enum SortColumn { Name = 0, Path = 1, Size = 2, Modified = 3 }

    public struct ResultRef : IEquatable<ResultRef>
    {
        public readonly int Volume;   // SearchResult.Indexes içindeki sıra
        public readonly int Id;       // MFT kayıt no

        public ResultRef(int volume, int id)
        {
            Volume = volume;
            Id = id;
        }

        public bool Equals(ResultRef other) { return Volume == other.Volume && Id == other.Id; }
        public override bool Equals(object obj) { return obj is ResultRef && Equals((ResultRef)obj); }
        public override int GetHashCode() { return (Volume * 397) ^ Id; }
    }

    public sealed class SearchResult
    {
        public FileIndex[] Indexes;
        public ResultRef[] Items;
        public int TotalLive;
        public long ElapsedMs;
        public string Error;

        /// <summary>Verilen girdilerin bu sonuçtaki sıra numaraları (seçimi korumak için).</summary>
        public List<int> IndexesOf(ICollection<ResultRef> refs)
        {
            var found = new List<int>();
            if (refs.Count == 0) return found;
            if (refs.Count == 1)
            {
                // Sık durum (tek seçili satır): karma tablosu yerine düz karşılaştırma, büyük listede çok daha hızlı
                ResultRef target = refs.First();
                for (int i = 0; i < Items.Length; i++)
                {
                    if (Items[i].Id == target.Id && Items[i].Volume == target.Volume)
                    {
                        found.Add(i);
                        break;
                    }
                }
                return found;
            }
            var wanted = new HashSet<ResultRef>(refs);
            for (int i = 0; i < Items.Length && wanted.Count > 0; i++)
                if (wanted.Remove(Items[i])) found.Add(i);
            return found;
        }
    }

    public static class SearchEngine
    {
        public static SearchResult Run(FileIndex[] indexes, Query query, FilterKind filter, SortColumn sort, bool descending, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var perVolume = new List<ResultRef[]>();
            int totalLive = 0;
            for (int v = 0; v < indexes.Length; v++)
            {
                ct.ThrowIfCancellationRequested();
                FileIndex idx = indexes[v];
                idx.Lock.EnterReadLock();
                try
                {
                    totalLive += idx.LiveCount;
                    int[] ids = ScanVolume(idx, query, filter, ct);
                    var refs = new ResultRef[ids.Length];
                    for (int i = 0; i < ids.Length; i++) refs[i] = new ResultRef(v, ids[i]);
                    perVolume.Add(refs);
                }
                finally { idx.Lock.ExitReadLock(); }
            }

            ResultRef[] items;
            EnterAll(indexes);
            try
            {
                items = MergeVolumes(indexes, perVolume, ct);
                if (sort != SortColumn.Name) items = SortByKey(indexes, items, sort, ct);
            }
            finally { ExitAll(indexes); }
            if (descending) Array.Reverse(items);

            var result = new SearchResult();
            result.Indexes = indexes;
            result.Items = items;
            result.TotalLive = totalLive;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Error = query.Error;
            return result;
        }

        /// <summary>
        /// Mevcut sonucu yalnızca değişen kayıtlarla günceller (tam taramaya göre çok ucuz). Değişiklik sonucu
        /// etkilemiyorsa aynı nesneyi döndürür. Yola bağlı sorgu/sıralamada bir klasör değiştiyse alt girdilerin
        /// yolu da değişir; bu durumda null döner ve tam arama gerekir.
        /// </summary>
        public static SearchResult Update(SearchResult current, int volume, ICollection<int> changedIds, Query query, FilterKind filter, SortColumn sort, bool descending)
        {
            if (changedIds.Count == 0) return current;
            var sw = Stopwatch.StartNew();
            FileIndex[] indexes = current.Indexes;
            FileIndex idx = indexes[volume];
            bool pathDependent = query.NeedsPath || sort == SortColumn.Path;
            int[] changed = new HashSet<int>(changedIds).ToArray();
            Array.Sort(changed);
            EnterAll(indexes);
            try
            {
                var additions = new List<ResultRef>();
                foreach (int id in changed)
                {
                    if (!idx.IsLive(id)) continue;
                    if (pathDependent && idx.IsDirectory(id)) return null;
                    if (Filters.Accept(filter, idx, id) && query.IsMatch(idx, id)) additions.Add(new ResultRef(volume, id));
                }

                // Sonuçta bulunan değişmiş kayıtların konumları (eski adları bilinmediği için tek doğrusal geçiş)
                ResultRef[] items = current.Items;
                var removedAt = new List<int>();
                int minId = changed[0], maxId = changed[changed.Length - 1];
                for (int i = 0; i < items.Length; i++)
                {
                    ResultRef item = items[i];
                    if (item.Volume != volume || item.Id < minId || item.Id > maxId) continue;
                    if (Array.BinarySearch(changed, item.Id) >= 0) removedAt.Add(i);
                }
                if (removedAt.Count == 0 && additions.Count == 0) return current;

                ResultRef[] kept = removedAt.Count == 0 ? items : WithoutPositions(items, removedAt);
                ResultRef[] merged = kept;
                if (additions.Count > 0)
                {
                    // Yeni kayıtların yeri ikili aramayla bulunur, aradaki bloklar topluca kopyalanır
                    Comparison<ResultRef> compare = OrderOf(indexes, sort, descending);
                    additions.Sort(compare);
                    merged = new ResultRef[kept.Length + additions.Count];
                    int src = 0, dst = 0;
                    foreach (var addition in additions)
                    {
                        int pos = UpperBound(kept, src, addition, compare);
                        Array.Copy(kept, src, merged, dst, pos - src);
                        dst += pos - src;
                        src = pos;
                        merged[dst++] = addition;
                    }
                    Array.Copy(kept, src, merged, dst, kept.Length - src);
                }

                var result = new SearchResult();
                result.Indexes = indexes;
                result.Items = merged;
                foreach (var each in indexes) result.TotalLive += each.LiveCount;
                result.ElapsedMs = sw.ElapsedMilliseconds;
                result.Error = current.Error;
                return result;
            }
            finally { ExitAll(indexes); }
        }

        /// <summary>
        /// Önceki sonucun içinden yeni sorguya uyanları süzer; sıra korunur. Query.CanNarrow doğruysa tam aramayla
        /// aynı sonucu verir ama yalnızca önceki sonuçları tarar (yazarken her tuşta tüm diski taramamak için).
        /// </summary>
        public static SearchResult Narrow(SearchResult previous, Query query, FilterKind filter, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            FileIndex[] indexes = previous.Indexes;
            ResultRef[] source = previous.Items;
            var result = new SearchResult();
            EnterAll(indexes);
            try
            {
                foreach (var idx in indexes) result.TotalLive += idx.LiveCount;
                int n = source.Length;
                int chunkCount = Math.Max(1, Math.Min(n / 4096 + 1, Environment.ProcessorCount * 4));
                int chunkSize = (n + chunkCount - 1) / chunkCount;
                var parts = new List<ResultRef>[chunkCount];
                var options = new ParallelOptions();
                options.CancellationToken = ct;
                Parallel.For(0, chunkCount, options, c =>
                {
                    var list = new List<ResultRef>();
                    int end = Math.Min(n, (c + 1) * chunkSize);
                    for (int i = c * chunkSize; i < end; i++)
                    {
                        if ((i & 0x3FFF) == 0 && ct.IsCancellationRequested) return;
                        ResultRef r = source[i];
                        FileIndex idx = indexes[r.Volume];
                        if (idx.IsLive(r.Id) && Filters.Accept(filter, idx, r.Id) && query.IsMatch(idx, r.Id)) list.Add(r);
                    }
                    parts[c] = list;
                });
                ct.ThrowIfCancellationRequested();
                int total = 0;
                foreach (var part in parts) total += part.Count;
                var items = new ResultRef[total];
                int k = 0;
                foreach (var part in parts)
                {
                    part.CopyTo(items, k);
                    k += part.Count;
                }
                result.Items = items;
            }
            finally { ExitAll(indexes); }
            result.Indexes = indexes;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Error = query.Error;
            return result;
        }

        /// <summary>from'dan itibaren value'dan kesin büyük ilk öğenin konumu (eşitlerin sonrasına ekler).</summary>
        static int UpperBound(ResultRef[] items, int from, ResultRef value, Comparison<ResultRef> compare)
        {
            int lo = from, hi = items.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (compare(value, items[mid]) < 0) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        static ResultRef[] WithoutPositions(ResultRef[] items, List<int> positions)
        {
            var result = new ResultRef[items.Length - positions.Count];
            int src = 0, dst = 0;
            foreach (int pos in positions)
            {
                Array.Copy(items, src, result, dst, pos - src);
                dst += pos - src;
                src = pos + 1;
            }
            Array.Copy(items, src, result, dst, items.Length - src);
            return result;
        }

        /// <summary>Tam aramanın ürettiği sırayla aynı karşılaştırma: anahtar, sonra ad, sonra disk sırası.</summary>
        static Comparison<ResultRef> OrderOf(FileIndex[] indexes, SortColumn sort, bool descending)
        {
            Comparison<ResultRef> byName = (a, b) =>
            {
                int c = FileIndex.CompareNames(indexes[a.Volume], a.Id, indexes[b.Volume], b.Id);
                return c != 0 ? c : a.Volume - b.Volume;
            };
            Comparison<ResultRef> order;
            switch (sort)
            {
                case SortColumn.Size:
                    order = (a, b) =>
                    {
                        int c = indexes[a.Volume].GetSize(a.Id).CompareTo(indexes[b.Volume].GetSize(b.Id));
                        return c != 0 ? c : byName(a, b);
                    };
                    break;
                case SortColumn.Modified:
                    order = (a, b) =>
                    {
                        int c = indexes[a.Volume].GetModified(a.Id).CompareTo(indexes[b.Volume].GetModified(b.Id));
                        return c != 0 ? c : byName(a, b);
                    };
                    break;
                case SortColumn.Path:
                    order = (a, b) =>
                    {
                        int c = string.Compare(indexes[a.Volume].GetDirectoryPath(a.Id), indexes[b.Volume].GetDirectoryPath(b.Id), StringComparison.OrdinalIgnoreCase);
                        return c != 0 ? c : byName(a, b);
                    };
                    break;
                default:
                    order = byName;
                    break;
            }
            if (!descending) return order;
            return (a, b) => order(b, a);
        }

        /// <summary>Bir diskin eşleşen girdileri, ada göre sıralı. Okuma kilidi çağıranda.</summary>
        static int[] ScanVolume(FileIndex idx, Query query, FilterKind filter, CancellationToken ct)
        {
            int[] sorted = idx.SortedByName;
            if (sorted == null)
            {
                var all = new int[idx.Capacity];
                for (int i = 0; i < all.Length; i++) all[i] = i;
                int[] found = ParallelFilter(idx, all, FileIndex.FlagLive, query, filter, ct);
                Array.Sort(found, (a, b) => FileIndex.CompareNames(idx, a, idx, b));
                return found;
            }
            int[] main = query.NeedsPath
                ? ParallelFilter(idx, sorted, (byte)(FileIndex.FlagLive | FileIndex.FlagInTail), query, filter, ct)
                : ParallelFilterSorted(idx, idx.Sorted, query, filter, ct);
            var tailMatches = new List<int>();
            foreach (int id in idx.TailSnapshot())
            {
                if ((idx.flags[id] & FileIndex.FlagLive) != 0 && Filters.Accept(filter, idx, id) && query.IsMatch(idx, id))
                    tailMatches.Add(id);
            }
            if (tailMatches.Count == 0) return main;
            tailMatches.Sort((a, b) => FileIndex.CompareNames(idx, a, idx, b));
            return MergeIds(idx, main, tailMatches);
        }

        /// <summary>
        /// Kaynak diziyi parçalara bölüp paralel süzer; parçalar sırayla birleştiği için kaynak sırası korunur.
        /// mask ile girdinin bayrakları maskelenince yalnızca FlagLive kalmalı.
        /// </summary>
        static int[] ParallelFilter(FileIndex idx, int[] source, byte mask, Query query, FilterKind filter, CancellationToken ct)
        {
            int n = source.Length;
            int chunkCount = Math.Max(1, Math.Min(n / 4096 + 1, Environment.ProcessorCount * 4));
            int chunkSize = (n + chunkCount - 1) / chunkCount;
            var parts = new List<int>[chunkCount];
            byte[] flags = idx.flags;
            var options = new ParallelOptions();
            options.CancellationToken = ct;
            Parallel.For(0, chunkCount, options, c =>
            {
                var list = new List<int>();
                int end = Math.Min(n, (c + 1) * chunkSize);
                for (int i = c * chunkSize; i < end; i++)
                {
                    if ((i & 0x3FFF) == 0 && ct.IsCancellationRequested) return;
                    int id = source[i];
                    if ((flags[id] & mask) != FileIndex.FlagLive) continue;
                    if (!Filters.Accept(filter, idx, id) || !query.IsMatch(idx, id)) continue;
                    list.Add(id);
                }
                parts[c] = list;
            });
            ct.ThrowIfCancellationRequested();
            int total = 0;
            foreach (var part in parts) total += part.Count;
            var result = new int[total];
            int k = 0;
            foreach (var part in parts)
            {
                part.CopyTo(result, k);
                k += part.Count;
            }
            return result;
        }

        /// <summary>
        /// Yol gerektirmeyen sorgular için: isimler sıralı tamponda art arda okunur (önbellek dostu); girdinin
        /// bayraklarına ve filtreye yalnızca isim eşleşince bakılır. Kuyruktaki ve silinmiş girdiler atlanır.
        /// </summary>
        static int[] ParallelFilterSorted(FileIndex idx, SortedView view, Query query, FilterKind filter, CancellationToken ct)
        {
            int[] ids = view.Ids;
            int[] offsets = view.Offsets;
            char[] names = view.Names;
            int n = ids.Length;
            int chunkCount = Math.Max(1, Math.Min(n / 4096 + 1, Environment.ProcessorCount * 4));
            int chunkSize = (n + chunkCount - 1) / chunkCount;
            var parts = new List<int>[chunkCount];
            byte[] flags = idx.flags;
            const byte mask = FileIndex.FlagLive | FileIndex.FlagInTail;
            var options = new ParallelOptions();
            options.CancellationToken = ct;
            Parallel.For(0, chunkCount, options, c =>
            {
                var list = new List<int>();
                int end = Math.Min(n, (c + 1) * chunkSize);
                for (int i = c * chunkSize; i < end; i++)
                {
                    if ((i & 0x3FFF) == 0 && ct.IsCancellationRequested) return;
                    int offset = offsets[i];
                    if (!query.IsMatchName(names, offset, offsets[i + 1] - offset)) continue;
                    int id = ids[i];
                    if ((flags[id] & mask) != FileIndex.FlagLive) continue;
                    if (!Filters.Accept(filter, idx, id)) continue;
                    list.Add(id);
                }
                parts[c] = list;
            });
            ct.ThrowIfCancellationRequested();
            int total = 0;
            foreach (var part in parts) total += part.Count;
            var result = new int[total];
            int k = 0;
            foreach (var part in parts)
            {
                part.CopyTo(result, k);
                k += part.Count;
            }
            return result;
        }

        static int[] MergeIds(FileIndex idx, int[] a, List<int> b)
        {
            var result = new int[a.Length + b.Count];
            int i = 0, j = 0, k = 0;
            while (i < a.Length && j < b.Count)
                result[k++] = FileIndex.CompareNames(idx, b[j], idx, a[i]) < 0 ? b[j++] : a[i++];
            while (i < a.Length) result[k++] = a[i++];
            while (j < b.Count) result[k++] = b[j++];
            return result;
        }

        static ResultRef[] MergeVolumes(FileIndex[] indexes, List<ResultRef[]> lists, CancellationToken ct)
        {
            if (lists.Count == 0) return new ResultRef[0];
            ResultRef[] acc = lists[0];
            for (int n = 1; n < lists.Count; n++)
            {
                ResultRef[] other = lists[n];
                var merged = new ResultRef[acc.Length + other.Length];
                int i = 0, j = 0, k = 0;
                while (i < acc.Length && j < other.Length)
                {
                    if ((k & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                    ResultRef x = acc[i], y = other[j];
                    merged[k++] = FileIndex.CompareNames(indexes[y.Volume], y.Id, indexes[x.Volume], x.Id) < 0 ? other[j++] : acc[i++];
                }
                while (i < acc.Length) merged[k++] = acc[i++];
                while (j < other.Length) merged[k++] = other[j++];
                acc = merged;
            }
            return acc;
        }

        /// <summary>Yol (klasör yolu), boyut veya tarihe göre; eşitlikte ad sırası korunur. Okuma kilitleri çağıranda.</summary>
        static ResultRef[] SortByKey(FileIndex[] indexes, ResultRef[] items, SortColumn sort, CancellationToken ct)
        {
            int n = items.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Comparison<int> compare;
            if (sort == SortColumn.Path)
            {
                var keys = new string[n];
                for (int i = 0; i < n; i++)
                {
                    if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
                    keys[i] = indexes[items[i].Volume].GetDirectoryPath(items[i].Id);
                }
                compare = (a, b) =>
                {
                    int c = string.Compare(keys[a], keys[b], StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : a - b;
                };
            }
            else
            {
                var keys = new long[n];
                for (int i = 0; i < n; i++)
                {
                    FileIndex idx = indexes[items[i].Volume];
                    keys[i] = sort == SortColumn.Size ? idx.GetSize(items[i].Id) : idx.GetModified(items[i].Id);
                }
                compare = (a, b) =>
                {
                    int c = keys[a].CompareTo(keys[b]);
                    return c != 0 ? c : a - b;
                };
            }
            Array.Sort(order, compare);
            var sorted = new ResultRef[n];
            for (int i = 0; i < n; i++) sorted[i] = items[order[i]];
            return sorted;
        }

        static void EnterAll(FileIndex[] indexes)
        {
            foreach (var idx in indexes) idx.Lock.EnterReadLock();
        }

        static void ExitAll(FileIndex[] indexes)
        {
            for (int i = indexes.Length - 1; i >= 0; i--) indexes[i].Lock.ExitReadLock();
        }
    }
}
