using System.Collections.Generic;
using System.Linq;
using Arama.Index;
using Arama.Search;

namespace Arama.Tests
{
    static class SearchFixture
    {
        // C:\
        //   Users\ (30)
        //     ali\ (31): notlar.txt (40), Öğrenci Listesi.xlsx (42), IMG_0012.jpg (43)
        //     readme.md (41)
        //   Projeler\ (32): rapor 2024.docx (44), rapor 2023.pdf (45), tmp_rapor.tmp (46)
        public static FileIndex BuildC(bool sorted)
        {
            var idx = new FileIndex('c', 64);
            AddDir(idx, 30, "Users", 5);
            AddDir(idx, 31, "ali", 30);
            AddDir(idx, 32, "Projeler", 5);
            AddFile(idx, 40, "notlar.txt", 31, 1234, 400);
            AddFile(idx, 41, "readme.md", 30, 10, 100);
            AddFile(idx, 42, "Öğrenci Listesi.xlsx", 31, 5000, 300);
            AddFile(idx, 43, "IMG_0012.jpg", 31, 300000, 700);
            AddFile(idx, 44, "rapor 2024.docx", 32, 20000, 600);
            AddFile(idx, 45, "rapor 2023.pdf", 32, 70000, 500);
            AddFile(idx, 46, "tmp_rapor.tmp", 32, 1, 200);
            idx.FinishLoad();
            if (sorted) idx.BuildSortedOrder();
            return idx;
        }

        // D:\ Ali Notları.txt (30), zeta.txt (31)
        public static FileIndex BuildD()
        {
            var idx = new FileIndex('d', 64);
            AddFile(idx, 30, "Ali Notları.txt", 5, 50, 900);
            AddFile(idx, 31, "zeta.txt", 5, 60, 800);
            idx.FinishLoad();
            idx.BuildSortedOrder();
            return idx;
        }

        /// <summary>Sorguyu SearchEngine olmadan uygular; ada göre sıralı isimler döner.</summary>
        public static List<string> Match(FileIndex idx, string query, FilterKind filter)
        {
            Query q = Query.Parse(query);
            return idx.SortedByName
                .Where(id => idx.IsLive(id) && Filters.Accept(filter, idx, id) && q.IsMatch(idx, id))
                .Select(id => idx.GetName(id))
                .ToList();
        }

        static void AddDir(FileIndex idx, int id, string name, int parent)
        {
            idx.LoadRecord(id, true, 1, 0x10);
            idx.LoadName(id, name, parent);
        }

        static void AddFile(FileIndex idx, int id, string name, int parent, long size, long modified)
        {
            idx.LoadRecord(id, false, modified, 0x20);
            idx.LoadName(id, name, parent);
            idx.LoadSize(id, size);
        }
    }
}
