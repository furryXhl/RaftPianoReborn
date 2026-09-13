using System;
using FMOD;
using FMODUnity;

namespace RaftPianoReborn
{
    internal sealed class AudioOutput : IDisposable
    {
        private readonly NativeAudio native;
        private readonly FMOD.System system;
        private DSP dsp;
        private Channel channel;
        private ChannelGroup group;
        private bool disposed;
        public AudioOutput(NativeAudio engine, FMOD.System audioSystem)
        {
            native = engine; system = audioSystem;
        }
        public void Start()
        {
            // Registration stays native end-to-end. No audio callback delegates
            // are marshalled through Mono, even during initialization.
            dsp.handle = native.CreateDSP(system.handle);
            Check(dsp.setChannelFormat(CHANNELMASK.STEREO, 2, SPEAKERMODE.STEREO), "DSP stereo format");
            Check(system.createChannelGroup("Raft Piano Reborn", out group), "create volume group");
            Check(system.playDSP(dsp, group, true, out channel), "play native DSP");
            Check(channel.setPriority(0), "piano priority");
            Check(channel.setMode(MODE._2D | MODE.LOOP_NORMAL), "piano channel mode");
            Check(dsp.setActive(true), "activate DSP");
            UpdateVolume();
            Check(channel.setPaused(false), "start continuous output");
        }
        public void UpdateVolume()
        {
            if (!group.hasHandle()) return;
            float master = 1, sfx = 1;
            RuntimeManager.GetVCA("vca:/master").getVolume(out master);
            RuntimeManager.GetVCA("vca:/sfx").getVolume(out sfx);
            group.setVolume(Math.Max(0, master) * Math.Max(0, sfx));
            // Keep the generator running even when muted, so key-up and tails are consumed.
            group.setMute(RuntimeManager.IsMuted);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; native.Enable(false);
            // FMOD serializes graph mutation against its mixer. Disconnect first,
            // and explicitly synchronize before freeing the native synth state.
            Check(system.lockDSP(), "lock mixer for teardown");
            try
            {
                if (channel.hasHandle()) { channel.stop(); channel.clearHandle(); }
                if (dsp.hasHandle()) { dsp.setActive(false); dsp.release(); dsp.clearHandle(); }
                if (group.hasHandle()) { group.release(); group.clearHandle(); }
            }
            finally { system.unlockDSP(); }
            native.Destroy();
        }
        internal static void Check(RESULT result, string step)
        {
            if (result != RESULT.OK) throw new InvalidOperationException(step + ": " + result);
        }
    }
}
