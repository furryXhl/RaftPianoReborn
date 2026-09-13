using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RaftPianoReborn
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputStatus
    {
        public ulong Polls, MaxGapUs;
        public uint Requested, Foreground, Active, Running;
        public uint LastVk, LastDown, HighResolutionTimer, HeldNotes;
        public string Summary()
        {
            return "input=direct_state polls=" + Polls + " max_poll_gap_us=" + MaxGapUs +
                " request=" + Requested + " foreground=" + Foreground + " active=" + Active +
                " running=" + Running + " last_vk=" + LastVk + " last_down=" + LastDown +
                " high_res_timer=" + HighResolutionTimer + " held_notes=" + HeldNotes;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStats
    {
        public ulong Blocks, Frames, Attacks, Releases, LateEvents, DroppedEvents;
        public ulong RenderOverruns, MaxRenderUs, NonFinite, PeaksOverOne;
        public ulong Conflicts, OutOfRange, Resets, HookEvents;
        public uint Voices, MaxVoices, Velocity, Enabled;
        public float Peak;
        public uint PedalDown;
        public string Summary()
        {
            return "blocks=" + Blocks + " frames=" + Frames + " attacks=" + Attacks +
                " releases=" + Releases + " late=" + LateEvents + " dropped=" + DroppedEvents +
                " overruns=" + RenderOverruns + " max_us=" + MaxRenderUs + " invalid=" + NonFinite +
                " over_0dB=" + PeaksOverOne + " peak=" + Peak.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                " voices=" + Voices + " max_voices=" + MaxVoices + " conflicts=" + Conflicts +
                " range_rejected=" + OutOfRange + " resets=" + Resets + " key_events=" + HookEvents + " pedal=" + PedalDown;
        }
    }
    internal sealed class NativeAudio
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(IntPtr path, int rate, int block);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDspDelegate(IntPtr system, out IntPtr dsp);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PointerDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureBindingsDelegate(
            [In] int[] noteOffsets, int noteCount, [In] int[] layerKeys, int layerCount,
            [In] int[] controlKeys, int controlCount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StartDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void EnableDelegate(int enabled);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StatsDelegate(out AudioStats stats);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InputStatusDelegate(out InputStatus status);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyDelegate();
        private CreateDelegate create;
        private CreateDspDelegate createDsp;
        private PointerDelegate error, callback;
        private ConfigureBindingsDelegate configureBindings;
        private StartDelegate start;
        private EnableDelegate enable;
        private StatsDelegate stats;
        private InputStatusDelegate inputStatus;
        private DestroyDelegate destroy;
        private bool created;
        // Native modules stay loaded until process exit. Never unload code that FMOD
        // or a Windows callback may still have referenced during mod teardown.
        private static IntPtr sfizzModule, coreModule;
        public NativeAudio(string folder)
        {
            if (sfizzModule == IntPtr.Zero) sfizzModule = Load(Path.Combine(folder, "sfizz.dll"));
            if (coreModule == IntPtr.Zero) coreModule = Load(Path.Combine(folder, "RaftPianoRebornCore.dll"));
            create = Bind<CreateDelegate>("rp_create"); error = Bind<PointerDelegate>("rp_error");
            createDsp = Bind<CreateDspDelegate>("rp_create_dsp");
            configureBindings = Bind<ConfigureBindingsDelegate>("rp_configure_bindings");
            callback = Bind<PointerDelegate>("rp_dsp_read"); start = Bind<StartDelegate>("rp_start_keyboard");
            enable = Bind<EnableDelegate>("rp_enable"); stats = Bind<StatsDelegate>("rp_stats");
            inputStatus = Bind<InputStatusDelegate>("rp_input_status");
            destroy = Bind<DestroyDelegate>("rp_destroy");
        }
        private static IntPtr Load(string path)
        {
            IntPtr value = LoadLibraryExW(path, IntPtr.Zero, 8); // Private dependency folder, Unicode path.
            if (value == IntPtr.Zero) throw new InvalidOperationException("Cannot load " + path + " (Windows " + Marshal.GetLastWin32Error() + ")");
            return value;
        }
        private static T Bind<T>(string name) where T : class
        {
            IntPtr p = GetProcAddress(coreModule, name);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        public void Initialize(string path, int rate, int block)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(path + "\0");
            IntPtr buffer = Marshal.AllocHGlobal(utf8.Length);
            try
            {
                Marshal.Copy(utf8, 0, buffer, utf8.Length);
                if (create(buffer, rate, block) == 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(error()));
                created = true;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public IntPtr ReadCallback { get { return callback(); } }
        public void ApplyBindings(ResolvedKeyboardBindings bindings)
        {
            if (!created) throw new InvalidOperationException("Piano engine is not initialized.");
            if (bindings == null) throw new ArgumentNullException("bindings");
            if (configureBindings(bindings.NoteOffsets, bindings.NoteOffsets.Length,
                bindings.LayerKeys, bindings.LayerKeys.Length,
                bindings.ControlKeys, bindings.ControlKeys.Length) == 0)
                throw new InvalidOperationException(Marshal.PtrToStringAnsi(error()));
        }
        public IntPtr CreateDSP(IntPtr system)
        {
            IntPtr dsp;
            if (createDsp(system, out dsp) != 0 || dsp == IntPtr.Zero)
                throw new InvalidOperationException(Marshal.PtrToStringAnsi(error()));
            return dsp;
        }
        public void StartKeyboard()
        {
            if (start() == 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(error()));
        }
        public void Enable(bool active) { if (created) enable(active ? 1 : 0); }
        public AudioStats GetStats() { AudioStats s; stats(out s); return s; }
        public InputStatus GetInputStatus() { InputStatus s; inputStatus(out s); return s; }
        public void Destroy() { if (created) { destroy(); created = false; } }
    }
}
