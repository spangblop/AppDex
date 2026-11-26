using System;
using System.Threading.Tasks;

namespace GameShelf
{
    internal sealed class PreferenceWriter
    {
        private readonly string path;
        private readonly Action<Exception> report;
        private readonly object gate = new object();
        private LibraryPreferences next;
        private Task worker = Task.CompletedTask;
        private bool writing;

        internal PreferenceWriter(string path, Action<Exception> report)
        {
            this.path = path;
            this.report = report;
        }

        internal void Queue(LibraryPreferences prefs)
        {
            var snapshot = prefs.Copy();
            lock (gate)
            {
                next = snapshot;
                if (writing) return;
                writing = true;
                worker = Task.Run((Action)WritePending);
            }
        }

        private void WritePending()
        {
            while (true)
            {
                LibraryPreferences snapshot;
                lock (gate)
                {
                    snapshot = next;
                    next = null;
                    if (snapshot == null) { writing = false; return; }
                }
                try { snapshot.Save(path); }
                catch (Exception error) { report(error); }
            }
        }

        internal void Flush()
        {
            Task current;
            lock (gate) current = worker;
            current.GetAwaiter().GetResult();
        }
    }
}
