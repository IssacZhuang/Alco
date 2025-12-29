using System.Threading.Tasks;

namespace Alco.LLM;

/// <summary>
/// Represents an abstract base class for LLM-related tasks.
/// </summary>
public abstract class LLMTask
{
    /// <summary>
    /// Executes the task asynchronously using the provided LLM context.
    /// </summary>
    /// <param name="context">The LLM context to use for execution.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public abstract Task Execute(LLMChat context);
}

