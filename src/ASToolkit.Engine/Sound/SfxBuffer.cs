using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal struct SfxBuffer
{
    public SfxBuffer(int id, int buffer)
    {
        Id = id;
        Buffer = buffer;
    }

    public int Id { get; set; }

    public int Buffer { get; set; }

    public bool IsPlaying { get; set; }

    public int Volume { get; set; }

    public int Pan { get; set; }

    public int Start { get; set; }

    [EngineRoutine(0x0041e530)]
    public void Destroy(Sound sound)
    {
        if (Buffer != Sound.NoBuffer)
        {
            sound.ReleaseBuffer(Buffer);
        }

        Buffer = Sound.NoBuffer;
        IsPlaying = false;
        Start = 0;
        Id = Sound.NoSound;
    }

    [EngineRoutine(0x0041e560)]
    public void Play(Sound sound, int index, int pan, int volume, int time, uint realTime)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer)
            return;

        Volume = volume;
        Pan = pan;
        ref var buffer = ref sound.GetBuffer(Buffer);
        sound.Device?.SetVoicePan(index, pan);
        buffer.Pan = pan;
        sound.Device?.SetVoiceVolume(index, volume);
        buffer.Volume = volume;

        if (!IsPlaying)
        {
            bool isLooping = sound.IsLooped(Id);

            if (sound.Device?.PlayVoice(index, isLooping) ?? false)
            {
                sound.Error?.Error($"SFXBUFFER[{Id}]", LogError.Text, 0, "buffer is lost"u8);
                sound.ReloadBuffers();
            }
            else
            {
                buffer.Play(isLooping, realTime);
            }
        }

        IsPlaying = true;
        Start = time;
    }

    [EngineRoutine(0x0041e650)]
    public void Resume(Sound sound, int index, uint realTime)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer || !IsPlaying)
            return;

        bool isLooping = sound.IsLooped(Id);

        if (sound.Device?.PlayVoice(index, isLooping) ?? false)
        {
            sound.Error?.Log("!!!ERROR!!! SFXBUFFER::buffer is lost"u8);
            sound.ReloadBuffers();
        }
        else
        {
            sound.GetBuffer(Buffer).Play(isLooping, realTime);
        }
    }

    [EngineRoutine(0x0041e6e0)]
    public bool CheckPlay(Sound sound, int index, int time, uint realTime)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer || !IsPlaying)
            return false;

        ref var buffer = ref sound.GetBuffer(Buffer);

        bool isTimedPlaying = buffer.GetStatus(realTime);

        if (!(sound.Device?.IsVoicePlaying(index) ?? isTimedPlaying))
        {
            IsPlaying = false;

            if (isTimedPlaying)
            {
                buffer.Stop(realTime);
            }
        }

        if (sound.IsLooped(Id) && (uint)(time - Start) > Sound.LoopHold)
        {
            IsPlaying = false;
            buffer.Stop(realTime);
        }

        return IsPlaying;
    }

    [EngineRoutine(0x0041e780)]
    public void Load(Sound sound, int id, int buffer)
    {
        Id = id;

        if (Buffer != Sound.NoBuffer && !(sound.IsDuplicate(buffer) && Buffer == buffer))
        {
            sound.ReleaseBuffer(Buffer);
        }

        Buffer = buffer;
        IsPlaying = false;
        Start = 0;
    }
}
