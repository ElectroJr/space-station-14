using Robust.Client.Graphics;

namespace Content.Client.Chameleon;

[RegisterComponent]
public sealed class ChameleonComponent : Component
{
    /// <summary>
    ///     Whether or not the entity previously had an interaction outline prior to cloaking.
    /// </summary>
    [DataField("hadOutline")]
    public bool HadOutline;
}
