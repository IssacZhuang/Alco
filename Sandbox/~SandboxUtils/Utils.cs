namespace SandboxUtils;

public static class Uitls
{
    public static string GetSolutionFolder()
    {
        string? current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            string[] files = Directory.GetFiles(current, "*.sln");
            if (files.Length > 0)
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new Exception("Solution file not found");
    }

    public static string GetBuiltInAssetsFolder()
    {
        return Path.Combine(GetSolutionFolder(), "Src", "Vocore.Engine", "Assets");
    }
}
