using System;
using Vocore;
using Vocore.Engine;
using Veldrid;

// See https://aka.ms/new-console-template for more information
// App app = new App(GraphicsBackend.OpenGL, "Test");
// app.Run();

using(Game game = new Game())
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}