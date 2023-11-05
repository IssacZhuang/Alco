using System;
using Veldrid;
using Veldrid.Sdl2;

#pragma warning disable CS8618
#pragma warning disable CS8625

namespace Vocore
{
    /// <summary>
    /// The entry point for the game
    /// </summary>
    public class GameEngine: IDisposable
    {
        private bool _isDisposed = false;

        private GraphicsDevice _graphicsDevice;
        private Sdl2Window _window;
        public static GameEngine Instance { get; private set; }

        public GameEngine()
        {
            if (Instance != null)
            {
                throw new Exception("The GameEngine can only have one instance.");
            }
            Instance = this;
            
        }

        ~GameEngine()
        {
            Dispose();
        }

        /// <summary>
        /// The game tick, which handles the game logic
        /// </summary>
        protected virtual void Tick(){

        }


        /// <summary>
        /// The render tick, which handles the game graphics
        /// </summary>
        protected virtual void Update(){

        }

        public void Dispose()
        {
            if(_isDisposed) return;
            _isDisposed = true;
            Instance = null;
        }
    }
}