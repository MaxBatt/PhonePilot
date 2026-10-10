// Local child-process fixture; never connects to an Android device.
if (args.Contains("--serial"))
{
    Console.WriteLine("INFO: Texture: 1280x720");
    await Task.Delay(30000);
    return;
}
switch (args.FirstOrDefault())
{
    case "echo-secret":
        var secret = await Console.In.ReadLineAsync();
        Console.WriteLine("stdin: " + secret);
        Console.Error.WriteLine("ERROR: " + secret);
        break;
    case "flood":
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 2500; i++) Console.WriteLine("out-" + i); }),
            Task.Run(() => { for (int i = 0; i < 2500; i++) Console.Error.WriteLine("err-" + i); }));
        break;
    case "delay":
        Console.WriteLine("started");
        await Task.Delay(30000);
        break;
    default:
        Console.WriteLine(string.Join("|", args));
        break;
}
