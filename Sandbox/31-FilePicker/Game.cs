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

        ImGui.SameLine();
        if (ImGui.Button(isPicking ? "Picking..." : "Open Folder Picker"))
        {
            if (!isPicking)
            {
                StartPickFolderAsync();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(isPicking ? "Picking..." : "Open Save Dialog"))
        {
            if (!isPicking)
            {
                StartSaveAsync();
            }
        }

        if (!string.IsNullOrEmpty(lastPickStatus))
        {
            ImGui.Separator();
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
            string defaultPath = AppContext.BaseDirectory;

            DialogFileFilter[] filters =
            {
                new DialogFileFilter("Images", "png;jpg;jpeg;bmp;tga"),
                new DialogFileFilter("All", "*")
            };

            string[] files = await ((Sdl3Window)MainView).OpenFilePickerAsync(defaultPath, true, filters);
            lastPickStatus = files.Length == 0 ? "Canceled" : string.Join("\n", files);
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

    private async void StartPickFolderAsync()
    {
        isPicking = true;
        lastPickStatus = "";
        try
        {
            string defaultPath = AppContext.BaseDirectory;
            string[] folders = await ((Sdl3Window)MainView).OpenFolderPickerAsync(defaultPath, true);
            lastPickStatus = folders.Length == 0 ? "Canceled" : string.Join("\n", folders);
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

    private async void StartSaveAsync()
    {
        isPicking = true;
        lastPickStatus = "";
        try
        {
            string defaultPath = System.IO.Path.Combine(AppContext.BaseDirectory, "untitled.txt");

            DialogFileFilter[] filters =
            {
                new DialogFileFilter("Text", "txt;log"),
                new DialogFileFilter("All", "*")
            };

            string[] result = await ((Sdl3Window)MainView).OpenSaveFilePickerAsync(defaultPath, filters);
            lastPickStatus = result.Length == 0 ? "Canceled" : string.Join("\n", result);
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


