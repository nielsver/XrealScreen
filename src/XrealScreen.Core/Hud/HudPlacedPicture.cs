namespace XrealScreen.Core.Hud;

/// <summary>
/// One image placed on the glasses picture. <see cref="Size"/> is its height in normalized device
/// coordinates, where the whole frame is 2 tall. The width follows the image aspect.
/// </summary>
public sealed record HudPlacedPicture(int Id, HudCaptionPicture Image, float CenterX, float CenterY, float Size);
