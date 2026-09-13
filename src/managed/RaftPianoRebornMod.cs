using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HMLLibrary;
using UnityEngine;
using FMODUnity;

public sealed class RaftPianoRebornMod : Mod
{
    private const string BuildVersion = "1.0.1-release";
    private RaftPianoReborn.NativeAudio native;
    private RaftPianoReborn.AudioOutput audio;
    private Instrument piano;
    private bool previousEnabled;
    private Network_Player pianist;
    private string state = "Extracting bundled piano...";
    private string cacheRoot, logPath, keyBindingsPath;
    private bool busy = true, ready, failed, cleaned, inputEnabled;
    private string inputBlockReason = "not_ready";
    private float nextScan, nextStatus, nextLog;
    private readonly RaftPianoReborn.PlayingMotion playingMotion = new RaftPianoReborn.PlayingMotion();
    private uint motionStarts, motionStops;
    private bool handAnimation;
    private RaftPianoReborn.AudioStats latest;
    private RaftPianoReborn.InputStatus inputStatus;
    private static readonly FieldInfo CurrentUser = typeof(Instrument).GetField("currentUser", BindingFlags.NonPublic | BindingFlags.Instance);

    public IEnumerator Start()
    {
        Log("RPR_START " + BuildVersion);
        try
        {
            // Mod.DataFolder is the loader's supported writable location. No installer
            // and no manual DLL/sample placement outside the single .rmod are needed.
            cacheRoot = Path.Combine(DataFolder, "content-" + BuildVersion);
            Directory.CreateDirectory(cacheRoot);
            logPath = Path.Combine(cacheRoot, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            keyBindingsPath = Path.Combine(DataFolder, RaftPianoReborn.KeyboardBindings.FileName);
            WriteLog("START " + BuildVersion + " cache=" + cacheRoot);
        }
        catch (Exception e) { Fail(e); }
        if (failed) { busy = false; yield break; }

        string[] lines = null;
        try { lines = Encoding.UTF8.GetString(Required("payload/manifest.tsv")).Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries); }
        catch (Exception e) { Fail(e); }
        if (failed) { busy = false; yield break; }
        for (int i = 0; i < lines.Length; ++i)
        {
            Task extraction = null;
            try
            {
                string[] fields = lines[i].TrimEnd('\r').Split('\t');
                if (fields.Length != 3) throw new InvalidDataException("Invalid payload manifest");
                string relative = fields[0];
                long length = long.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
                string expected = fields[2];
                string target = Path.GetFullPath(Path.Combine(cacheRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(Path.GetFullPath(cacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Payload path escaped the cache");
                state = "Preparing samples " + (i + 1) + "/" + lines.Length;
                // Cache verification and all file writes stay off the Unity frame.
                Task<bool> check = Task.Run(() => VerifyFile(target, length, expected));
                extraction = check;
            }
            catch (Exception e) { Fail(e); }
            if (failed) break;
            while (!extraction.IsCompleted) yield return null;
            if (extraction.IsFaulted) { Fail(extraction.Exception); break; }
            if (((Task<bool>)extraction).Result) continue;
            try
            {
                string[] fields = lines[i].TrimEnd('\r').Split('\t');
                string target = Path.Combine(cacheRoot, fields[0].Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = Required("payload/" + fields[0] + ".bin");
                if (bytes.LongLength != long.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture))
                    throw new InvalidDataException("Bundled file length differs: " + fields[0]);
                string expected = fields[2];
                extraction = Task.Run(() =>
                {
                    if (Hash(bytes) != expected) throw new InvalidDataException("Bundled file checksum differs: " + target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + ".partial";
                    File.WriteAllBytes(temp, bytes);
                    if (File.Exists(target)) File.Delete(target); // Only a verified private cache target.
                    File.Move(temp, target);
                });
            }
            catch (Exception e) { Fail(e); }
            if (failed) break;
            while (!extraction.IsCompleted) yield return null;
            if (extraction.IsFaulted) { Fail(extraction.Exception); break; }
            if ((i % 40) == 0) WriteLog(state);
        }
        if (failed) { busy = false; yield break; }

        Task load = null;
        FMOD.System core = RuntimeManager.CoreSystem;
        int rate = 0, speakers;
        uint frames = 0;
        int buffers;
        FMOD.SPEAKERMODE mode;
        try
        {
            RaftPianoReborn.AudioOutput.Check(core.getSoftwareFormat(out rate, out mode, out speakers), "read audio format");
            RaftPianoReborn.AudioOutput.Check(core.getDSPBufferSize(out frames, out buffers), "read DSP size");
            WriteLog("AUDIO rate=" + rate + " DSP=" + frames + "x" + buffers + " quality=7 voices=256 hint_ram_based=1 gain=0.525 release_cc72=0.8 release_regions_sustain=off idle_reclaim_dbfs=-120 idle_reclaim_seconds=2 motion_attack_window=1.0 held_extends_motion=0 status_overlay=0");
            state = "Loading all piano samples into RAM...";
            native = new RaftPianoReborn.NativeAudio(Path.Combine(cacheRoot, "native"));
            string bindingNotice;
            RaftPianoReborn.ResolvedKeyboardBindings bindings =
                RaftPianoReborn.KeyboardBindings.LoadOrCreate(keyBindingsPath, out bindingNotice);
            WriteLog("KEY_BINDINGS " + bindingNotice + " source=" + bindings.Source);
            int sampleRate = rate, block = checked((int)frames);
            load = Task.Run(() =>
            {
                native.Initialize(Path.Combine(cacheRoot, "Salamander.sfz"), sampleRate, block);
                native.ApplyBindings(bindings);
            });
        }
        catch (Exception e) { Fail(e); }
        if (failed) { busy = false; yield break; }
        while (!load.IsCompleted) yield return null;
        if (load.IsFaulted) { Fail(load.Exception); busy = false; yield break; }
        try
        {
            audio = new RaftPianoReborn.AudioOutput(native, core);
            audio.Start();
            WriteLog("NATIVE_DSP_REGISTERED no_managed_audio_delegate=1");
            native.StartKeyboard();
            WriteLog("DIRECT_INPUT_STARTED " + native.GetInputStatus().Summary());
            ready = true; state = "Ready. Sit at the piano.";
            WriteLog("READY " + BuildVersion);
        }
        catch (Exception e) { Fail(e); Cleanup(); }
        busy = false;
    }
    private byte[] Required(string name)
    {
        byte[] bytes = GetEmbeddedFileBytes(name);
        if (bytes == null) throw new FileNotFoundException("Missing embedded resource " + name);
        return bytes;
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static bool VerifyFile(string path, long length, string hash)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != length) return false;
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == hash;
    }
    public void Update()
    {
        if (!ready) return;
        try
        {
            if (piano != null && !piano.CanBeInteractedWith) Detach();
            if (piano == null && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.1f;
                foreach (Instrument item in UnityEngine.Object.FindObjectsOfType<Instrument>())
                {
                    if (item != null && item.GetType() == typeof(Instrument) && item.CanBeInteractedWith)
                    {
                        piano = item; previousEnabled = item.enabled; item.enabled = false;
                        playingMotion.Reset(native.GetStats().Attacks);
                        if (CurrentUser != null) pianist = CurrentUser.GetValue(item) as Network_Player;
                        WriteLog("SEATED id=" + item.GetInstanceID()); break;
                    }
                }
            }
            MenuType menu = CanvasHelper.ActiveMenu;
            bool focused = Application.isFocused;
            bool chat = ChatTextFieldController.IsChatWindowSelected;
            string blockReason = RaftPianoReborn.InputGate.BlockReason(piano != null, focused, chat, menu);
            bool allow = blockReason == null;
            if (allow != inputEnabled || blockReason != inputBlockReason)
            {
                if (allow != inputEnabled) native.Enable(allow);
                inputEnabled = allow; inputBlockReason = blockReason;
                WriteLog("INPUT_GATE allow=" + allow + " seated=" + (piano != null) +
                    " focus=" + focused + " menu=" + menu + " chat=" + chat +
                    " reason=" + (blockReason ?? "allowed"));
            }
            if (Time.unscaledTime >= nextStatus)
            {
                nextStatus = Time.unscaledTime + 0.05f;
                latest = native.GetStats();
                inputStatus = native.GetInputStatus();
                bool animate = playingMotion.Update(Time.unscaledTime, latest.Attacks,
                    allow && inputStatus.Active != 0);
                if (animate != handAnimation)
                {
                    handAnimation = animate;
                    if (animate) ++motionStarts; else ++motionStops;
                    if (pianist != null && pianist.Animator != null && pianist.Animator.anim != null)
                        pianist.Animator.anim.SetBool("PlayingInstrumentNote", animate);
                }
            }
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5;
                audio.UpdateVolume(); WriteLog("STATS " + latest.Summary() + " | " + inputStatus.Summary() +
                    " | motion_starts=" + motionStarts + " motion_stops=" + motionStops);
            }
        }
        catch (Exception e) { Fail(e); Cleanup(); }
    }
    private void Detach()
    {
        if (native != null) native.Enable(false);
        inputEnabled = false;
        inputBlockReason = "not_seated";
        playingMotion.Reset(latest.Attacks);
        if (pianist != null && pianist.Animator != null && pianist.Animator.anim != null)
            pianist.Animator.anim.SetBool("PlayingInstrumentNote", false);
        handAnimation = false; pianist = null;
        if (piano != null) { piano.enabled = previousEnabled; WriteLog("LEFT_PIANO " + latest.Summary()); }
        piano = null;
    }
    public override bool CanUnload(ref string message)
    {
        if (busy) { message = "Piano samples are still loading. Please wait for Ready or an error."; return false; }
        return true;
    }
    public void OnModUnload() { Cleanup(); Destroy(gameObject); }
    public void OnDestroy() { if (!busy) Cleanup(); }
    private void Cleanup()
    {
        if (cleaned) return;
        cleaned = true; ready = false; Detach();
        try
        {
            if (audio != null) audio.Dispose();
            else if (native != null) native.Destroy();
            audio = null; native = null;
            WriteLog("STOPPED");
        }
        catch (Exception e) { WriteLog("TEARDOWN_ERROR " + e); }
    }
    private void Fail(Exception e)
    {
        failed = true; ready = false; state = "Piano error: " + e.GetBaseException().Message;
        WriteLog("ERROR " + e); Log(state);
    }
    private void WriteLog(string message)
    {
        if (logPath == null) return;
        try { File.AppendAllText(logPath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine, Encoding.UTF8); }
        catch { /* Logging must not disable the instrument. */ }
    }
    public void OnGUI()
    {
        // Only startup progress or an actual error is shown. There is no
        // in-performance status overlay or keyboard shortcut to reopen it.
        if (!busy && !failed) return;
        GUI.Box(new Rect(20, 20, 820, 58), "Raft Piano Reborn " + BuildVersion + "\n" + state);
    }
}
