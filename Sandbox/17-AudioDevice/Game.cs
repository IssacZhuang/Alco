using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.GUI;


public class Game : GameEngine
{
    private AudioDevice _device;
    private AudioClip _clip;
    private AudioSource _source;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _device = AudioDeviceFactory.CreateOpenALDevice();
        _clip = UtilsAudioDecode.CreateAudioClipFromOgg(LoadFile("Shot.ogg"));
        _source = _device.CreateSource();
        _source.AudioClip = _clip;
    }

    protected override void OnUpdate(float delta)
    {
        if (DebugGUI.Button("play"))
        {
            _source.Play();
        }
    }

    protected override void OnStop()
    {
        
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}