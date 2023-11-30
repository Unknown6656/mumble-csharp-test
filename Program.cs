using System.Net.Sockets;
using System.Net;

using MumbleSharp.Audio.Codecs;
using MumbleSharp.Audio;
using MumbleSharp.Model;
using MumbleSharp;
using MumbleProto;

using NAudio.Wave;

namespace Unknown6656.MumbleTest;


public static class TestProgram
{
    private static string SERVER_HOST = "127.0.0.1";
    private static ushort SERVER_PORT = 64738;
    private static string USER_NAME = "SuperUser";


    private static void Read(string prompt, ref string variable)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"Please enter the {prompt} or keep the line blank in order to keep the default value '{variable}'.");
        Console.ForegroundColor = ConsoleColor.White;

        if (Console.ReadLine()?.Trim() is { Length: > 0 } s)
            variable = s;

        Console.ForegroundColor = ConsoleColor.Gray;
    }

    public static async Task Main()
    {
        string port = SERVER_PORT.ToString();
        string pass = "";

        Read("server name", ref SERVER_HOST);
        Read("server port", ref port);
        Read("user name", ref USER_NAME);
        Read("user password", ref pass);

        if (ushort.TryParse(port, out ushort p) && p > 0)
            SERVER_PORT = p;
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Invalid port '{port}'");
            Console.ForegroundColor = ConsoleColor.Gray;

            return;
        }


        IPEndPoint server_ep = new(Dns.GetHostAddresses(SERVER_HOST).First(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6), SERVER_PORT);
        TestProtocol protocol = new();
        MumbleConnection connection = new(server_ep, protocol);

        connection.Connect(USER_NAME, pass, [], SERVER_HOST);

        TestRecorder recorder = new(protocol);
        Task update_loop = Task.Factory.StartNew(async () => await UpdateLoop(connection));

        Console.WriteLine("Connecting...");

        while (!protocol.ReceivedServerSync)
            await Task.Delay(100);

        Console.WriteLine($"Connected as {protocol.LocalUser.Id}.");

        PrintChannel([.. protocol.Channels], [.. protocol.Users], protocol.RootChannel);

        Console.WriteLine("""
        ,---------------------------------------------,
        | MUMBLE CLIENT STARTED. PRESS [ESC] TO QUIT. |
        '---------------------------------------------'
        """);

        do
            while (!Console.KeyAvailable)
                await Task.Delay(50);
        while (Console.ReadKey(true).Key is ConsoleKey.Escape);
    }

    private static async Task<Exception?> UpdateLoop(MumbleConnection connection)
    {
        try
        {
            while (connection.State != ConnectionStates.Disconnected)
                if (connection.Process())
                    await Task.Yield();
                else
                    await Task.Delay(10);

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection to {connection.Host} ({connection.State}) raised an exception: {ex}");

            return ex;
        }
    }

    private static void PrintChannel(Channel[] all_channels, User[] all_users, Channel channel, string indent = "")
    {
        Console.WriteLine($"{indent}{(channel.Temporary ? "[temp] " : "")} {channel.Name}");

        foreach (Channel c in all_channels.Where(ch => ch.Parent == channel.Id && ch.Parent != ch.Id))
            PrintChannel(all_channels, all_users, c, indent + "    ");

        foreach (User user in all_users.Where(u => u.Channel.Equals(channel)))
            Console.WriteLine($"{indent}- {user.Name} ({user.Comment?.Trim() ?? ""})");
    }
}

public sealed class TestRecorder
{
    private readonly IMumbleProtocol _protocol;
    private readonly WaveInEvent _input_stream;
    private bool _recording;


    public Channel? VoiceTarget { get; set; }


    public TestRecorder(IMumbleProtocol protocol)
    {
        _protocol = protocol;
        _input_stream = new()
        {
            WaveFormat = new(Constants.DEFAULT_AUDIO_SAMPLE_RATE, Constants.DEFAULT_AUDIO_SAMPLE_BITS, Constants.DEFAULT_AUDIO_SAMPLE_CHANNELS)
        };
        _input_stream.DataAvailable += VoiceDataAvailable;

        VoiceTarget = _protocol.LocalUser.Channel;

        Record();
    }

    private void VoiceDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_recording && VoiceTarget is Channel channel)
            channel.SendVoice(e.Buffer);
    }

    public void Record()
    {
        _recording = true;
        _input_stream.StartRecording();
    }

    public void Stop()
    {
        _recording = false;

        _protocol.LocalUser.Channel.SendVoiceStop();
        _input_stream.StopRecording();
    }
}

public sealed class TestPlayback
{
    private readonly WaveOutEvent _playback_device = new();


    public TestPlayback(IWaveProvider provider)
    {
        _playback_device.Init(provider);
        _playback_device.PlaybackStopped += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Playback stopped: {args.Exception}");
            Console.ForegroundColor = ConsoleColor.Gray;
        };
        _playback_device.Play();
    }
}

public sealed class TestProtocol
    : BasicMumbleProtocol
{
    private readonly Dictionary<User, TestPlayback> _users = [];


    public override void EncodedVoice(byte[] data, uint userId, long sequence, IVoiceCodec codec, SpeechTarget target)
    {
        User? user = Users.FirstOrDefault(u => u.Id == userId);

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"{user?.Name ?? "<unknown>"} ({user?.Id ?? userId:x8}) is speaking. Sequence #{sequence} ({data.Length} Bytes), {target}");
        Console.ForegroundColor = ConsoleColor.Gray;

        base.EncodedVoice(data, userId, sequence, codec, target);
    }

    protected override void UserJoined(User user)
    {
        base.UserJoined(user);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"{user.Name} ({user.Id:x8}) joined.");
        Console.ForegroundColor = ConsoleColor.Gray;

        _users.Add(user, new TestPlayback(user.Voice));
    }

    protected override void UserLeft(User user)
    {
        base.UserLeft(user);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{user.Name} ({user.Id:x8}) left.");
        Console.ForegroundColor = ConsoleColor.Gray;

        _users.Remove(user);
    }

    public override void ServerConfig(ServerConfig serverConfig)
    {
        base.ServerConfig(serverConfig);

        Console.WriteLine(serverConfig.WelcomeText);
    }

    protected override void ChannelMessageReceived(ChannelMessage message)
    {
        if (message.Channel.Equals(LocalUser.Channel))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[channel]  {message.Sender.Name,30}: {message.Text}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"[channel] {message.Sender.Name,30} @ {message.Channel.Name}: {message.Text}");
        }

        Console.ForegroundColor = ConsoleColor.Gray;

        base.ChannelMessageReceived(message);
    }

    protected override void PersonalMessageReceived(PersonalMessage message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[personal] {message.Sender.Name,30}: {message.Text}");
        Console.ForegroundColor = ConsoleColor.Gray;

        base.PersonalMessageReceived(message);
    }
}
