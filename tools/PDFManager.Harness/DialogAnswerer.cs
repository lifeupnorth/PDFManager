using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PDFManager.Harness
{
    // How to answer one dialog: by posting a button's command, or by typing a file name
    internal sealed record Answer(Action<IntPtr> Respond)
    {
        public const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;

        // An OK-only message box ignores a posted IDOK; post IDCANCEL to close it
        public static Answer Button(int id) => new Answer(h => Native.PostMessage(h, Native.WM_COMMAND, (IntPtr)id, IntPtr.Zero));

        // Types into the Open or Save As file-name box and presses OK. Several files go in as
        // "C:\full\a.pdf" "C:\full\b.pdf"
        public static Answer FileName(string text) => new Answer(h =>
        {
            IntPtr edit = Native.FindFileNameBox(h);
            if (edit == IntPtr.Zero)
                return;
            Native.SendMessage(edit, Native.WM_SETTEXT, IntPtr.Zero, text);
            Native.PostMessage(h, Native.WM_COMMAND, (IntPtr)IDOK, IntPtr.Zero);
        });
    }

    // Watches for this process's dialogs from a background thread and answers each one the
    // next expectation describes. Answers are chosen by the dialog's title and text, never by
    // order alone: a blind "next answer" once landed on "Open the output folder?" and opened
    // Explorer on the user's desktop. A dialog nobody expected is closed with No/Cancel.
    internal sealed class DialogAnswerer : IDisposable
    {
        private sealed record Expectation(string Name, Func<string, string, bool> Matches, Answer Answer);

        private readonly Report _report;
        private readonly Queue<Expectation> _expected = new Queue<Expectation>();
        private readonly object _sync = new object();
        private readonly Thread _thread;
        private volatile bool _stop;

        public DialogAnswerer(Report report)
        {
            _report = report;
            _thread = new Thread(Watch) { IsBackground = true, Name = "Harness dialog answerer" };
            _thread.Start();
        }

        public int Unexpected { get; private set; }

        public int Pending
        {
            get { lock (_sync) return _expected.Count; }
        }

        // The answer for the next dialog, which must match (title, text)
        public void Expect(string name, Func<string, string, bool> matches, Answer answer)
        {
            lock (_sync)
                _expected.Enqueue(new Expectation(name, matches, answer));
        }

        public async Task AllHandled(int timeoutMs = 20000)
        {
            var waited = Stopwatch.StartNew();
            while (Pending > 0 && waited.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(50);
            await Task.Delay(150); // let the dialog's modal loop unwind
        }

        public void Stop()
        {
            _stop = true;
            _thread.Join(2000);
        }

        public void Dispose() => Stop();

        private void Watch()
        {
            IntPtr current = IntPtr.Zero;
            Expectation? answering = null;
            while (!_stop)
            {
                Thread.Sleep(100);
                var dialogs = Native.OwnDialogs();
                if (answering != null)
                {
                    // Re-post until it closes: an answer posted too early can be lost.
                    // Message box windows are reused, so a handle alone does not identify one
                    if (dialogs.Contains(current))
                    {
                        answering.Answer.Respond(current);
                        continue;
                    }
                    lock (_sync)
                        _expected.Dequeue();
                    answering = null;
                }
                if (dialogs.Count == 0)
                    continue;

                IntPtr dialog = dialogs[0];
                string title = Native.Title(dialog), text = Native.MessageText(dialog);
                string shown = $"[{title}] {text.Replace("\n", " | ")}";
                Expectation? next;
                lock (_sync)
                    next = _expected.Count > 0 ? _expected.Peek() : null;
                if (next != null && next.Matches(title, text))
                {
                    _report.Log($"  dialog: {next.Name}: {shown}");
                    answering = next;
                    current = dialog;
                    next.Answer.Respond(dialog);
                }
                else
                {
                    Unexpected++;
                    _report.Log($"  UNEXPECTED dialog: {shown}");
                    Native.PostMessage(dialog, Native.WM_COMMAND, (IntPtr)Answer.IDNO, IntPtr.Zero);
                    Native.PostMessage(dialog, Native.WM_COMMAND, (IntPtr)Answer.IDCANCEL, IntPtr.Zero);
                    Thread.Sleep(300);
                }
            }
        }
    }

    internal static class Native
    {
        public const uint WM_COMMAND = 0x0111, WM_SETTEXT = 0x000C, WM_CLOSE = 0x0010;

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDlgItemText(IntPtr hwnd, int id, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, string lParam);

        private static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        public static string Title(IntPtr hwnd)
        {
            var text = new StringBuilder(512);
            GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        // A message box's text is its static control 0xFFFF; file dialogs have none
        public static string MessageText(IntPtr hwnd)
        {
            var text = new StringBuilder(8192);
            GetDlgItemText(hwnd, 0xFFFF, text, text.Capacity);
            return text.ToString().Replace("\r\n", "\n");
        }

        // Visible standard dialogs (#32770) of this process: message boxes and file dialogs
        public static List<IntPtr> OwnDialogs()
        {
            uint self = (uint)Environment.ProcessId;
            var found = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == self && IsWindowVisible(hwnd) && ClassOf(hwnd) == "#32770")
                    found.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // The file-name box of an Open or Save As dialog: an Edit inside a ComboBox
        public static IntPtr FindFileNameBox(IntPtr dialog)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(dialog, (hwnd, _) =>
            {
                if (ClassOf(hwnd) == "Edit" && ClassOf(GetParent(hwnd)) == "ComboBox" && IsWindowVisible(hwnd))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
