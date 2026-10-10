using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace Arama.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    /// <summary>Yönetici olarak çalışmayı gerektiren test; değilse atlanır.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AdminTestAttribute : Attribute { }

    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException(message + " | beklenen: <" + expected + ">, gelen: <" + actual + ">");
        }

        public static void SequenceEqual<T>(IList<T> expected, IList<T> actual, string message)
        {
            if (actual == null || !expected.SequenceEqual(actual))
                throw new AssertException(message + " | beklenen: [" + string.Join(", ", expected) + "], gelen: [" +
                    (actual == null ? "null" : string.Join(", ", actual)) + "]");
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new AssertException(message + " | " + typeof(TException).Name + " bekleniyordu");
        }
    }

    public static class TestRunner
    {
        static readonly StringBuilder Log = new StringBuilder();

        public static bool IsAdmin()
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>Ölçüm gibi bilgi satırlarını çıktıya ekler.</summary>
        public static void Info(string text)
        {
            Line("         " + text);
        }

        public static int Main(string[] args)
        {
            string filter = null, outPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                else filter = args[i];
            }
            bool admin = IsAdmin();
            int passed = 0, failed = 0, skipped = 0;
            var methods = typeof(TestRunner).Assembly.GetTypes()
                .Where(t => t.Namespace == "Arama.Tests")
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.IsDefined(typeof(TestAttribute), false) || m.IsDefined(typeof(AdminTestAttribute), false))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.MetadataToken)
                .ToList();
            foreach (var method in methods)
            {
                string name = method.DeclaringType.Name + "." + method.Name;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (method.IsDefined(typeof(AdminTestAttribute), false) && !admin)
                {
                    skipped++;
                    Line("ATLANDI  " + name + " (yonetici gerekli)");
                    continue;
                }
                var sw = Stopwatch.StartNew();
                try
                {
                    method.Invoke(null, null);
                    passed++;
                    Line("GECTI    " + name + " (" + sw.ElapsedMilliseconds + " ms)");
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Exception inner = ex.InnerException;
                    Line("KALDI    " + name);
                    Line("         " + (inner is AssertException ? inner.Message : inner.ToString()));
                }
            }
            Line("");
            Line("Toplam: " + passed + " gecti, " + failed + " kaldi, " + skipped + " atlandi");
            if (outPath != null) File.WriteAllText(outPath, Log.ToString(), Encoding.UTF8);
            return failed > 0 ? 1 : 0;
        }

        static void Line(string text)
        {
            Console.WriteLine(text);
            Log.AppendLine(text);
        }
    }
}
