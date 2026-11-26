using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal struct SfxBuffer
{
    public SfxBuffer(int index, int id, int buffer)
    {
        Index = index;
        Id = id;
        Buffer = buffer;
    }

    public int Index { get; }

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
    public void Play(Sound sound, int pan, int volume)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer)
            return;

        Volume = volume;
        Pan = pan;
        ref var buffer = ref sound.GetBuffer(Buffer);
        sound.Device?.SetVoicePan(Index, pan);
        buffer.Pan = pan;
        sound.Device?.SetVoiceVolume(Index, volume);
        buffer.Volume = volume;

        if (!IsPlaying)
        {
            bool isLooping = sound.IsLooped(Id);

            if (sound.Device?.PlayVoice(Index, isLooping) ?? false)
            {
                sound.Error?.Error($"SFXBUFFER[{Id}]", LogError.Text, 0, "buffer is lost"u8);
                sound.ReloadBuffers();
            }
            else
            {
                buffer.Play(isLooping, sound.RealTime);
            }
        }

        IsPlaying = true;
        Start = sound.Time;
    }

    [EngineRoutine(0x0041e650)]
    public void Resume(Sound sound)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer || !IsPlaying)
            return;

        bool isLooping = sound.IsLooped(Id);

        if (sound.Device?.PlayVoice(Index, isLooping) ?? false)
        {
            sound.Error?.Log("!!!ERROR!!! SFXBUFFER::buffer is lost"u8);
            sound.ReloadBuffers();
        }
        else
        {
            sound.GetBuffer(Buffer).Play(isLooping, sound.RealTime);
        }
    }

    [EngineRoutine(0x0041e6e0)]
    public bool CheckPlay(Sound sound)
    {
        if (Id < 0 || Buffer == Sound.NoBuffer || !IsPlaying)
            return false;

        ref var buffer = ref sound.GetBuffer(Buffer);

        bool isTimedPlaying = buffer.GetStatus(sound.RealTime);

        if (!(sound.Device?.IsVoicePlaying(Index) ?? isTimedPlaying))
        {
            IsPlaying = false;

            if (isTimedPlaying)
            {
                buffer.Stop(sound.RealTime);
            }
        }

        if (sound.IsLooped(Id) && (uint)(sound.Time - Start) > Sound.LoopHold)
        {
            IsPlaying = false;
            buffer.Stop(sound.RealTime);
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
