using System.Numerics;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;

public class Game : GameEngine
{
    private bool showWindow = true;
    private bool isPicking;
    private string lastPickStatus = "";

    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        RenderImGUIContent();
    }

    private void RenderImGUIContent()
    {
        if (!showWindow)
        {
            return;
        }

        ImGui.Begin("File Picker Sandbox", ref showWindow);

        if (ImGui.Button(isPicking ? "Picking..." : "Open File Picker"))
        {
            if (!isPicking)
            {
                StartPickAsync();
            }
        }

        if (!string.IsNullOrEmpty(lastPickStatus))
        {
            ImGui.TextWrapped(lastPickStatus);
        }

        ImGui.End();
    }

    private async void StartPickAsync()
    {
        isPicking = true;
        lastPickStatus = "";
        try
        {
            // Use executable directory as default path
            string defaultPath = AppContext.BaseDirectory;

            DialogFileFilter[] filters =
            {
                new DialogFileFilter("Images", "png;jpg;jpeg;bmp;tga"),
                new DialogFileFilter("All", "*")
            };

            string[] files = await ((Sdl3Window)MainView).OpenFilePickerAsync(defaultPath, true, filters);
            if (files.Length == 0)
            {
                lastPickStatus = "Canceled";
            }
            else
            {
                lastPickStatus = string.Join("\n", files);
            }
        }
        catch (System.Exception ex)
        {
            lastPickStatus = ex.Message;
        }
        finally
        {
            isPicking = false;
        }
    }
}


