using System.Collections.Generic;
using System.Linq;

namespace Arama.Index
{
    /// <summary>
    /// Arayüzün henüz işlemediği değişen kayıt numaraları (izleyici iş parçacığı ekler, arayüz alır).
    /// Çok birikirse ya da indeks yeniden kurulduysa liste tutulmaz, "tam arama gerekir" denir.
    /// </summary>
    public sealed class ChangeSet
    {
        readonly object sync = new object();
        readonly int limit;
        HashSet<int> ids = new HashSet<int>();
        bool fullSearch;

        public ChangeSet(int limit)
        {
            this.limit = limit;
        }

        public void Add(IEnumerable<int> changed)
        {
            lock (sync)
            {
                if (fullSearch) return;
                foreach (int id in changed)
                {
                    ids.Add(id);
                    if (ids.Count > limit)
                    {
                        RequireFullSearchLocked();
                        return;
                    }
                }
            }
        }

        public void RequireFullSearch()
        {
            lock (sync) RequireFullSearchLocked();
        }

        /// <summary>Biriken değişiklikleri alır ve sıfırlar. false: tam arama gerekir (ids boş).</summary>
        public bool Take(out int[] changed)
        {
            lock (sync)
            {
                bool incremental = !fullSearch;
                changed = ids.ToArray();
                ids = new HashSet<int>();
                fullSearch = false;
                return incremental;
            }
        }

        void RequireFullSearchLocked()
        {
            fullSearch = true;
            ids = new HashSet<int>();
        }
    }
}
