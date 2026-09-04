using OrderedClicker.Models;

namespace OrderedClicker.Core;

public sealed class SafetyCornerService
{
    private DateTimeOffset? _enteredAt;

    public bool Update(
        Point cursor,
        ScreenBounds virtualScreen,
        SafetyCorner corner,
        int size,
        TimeSpan dwell,
        DateTimeOffset now)
    {
        if (size <= 0 || dwell <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "安全角参数无效。");
        }

        if (!Contains(cursor, virtualScreen, corner, size))
        {
            _enteredAt = null;
            return false;
        }

        _enteredAt ??= now;
        return now - _enteredAt.Value >= dwell;
    }

    public void Reset()
    {
        _enteredAt = null;
    }

    public static bool Contains(
        Point cursor,
        ScreenBounds bounds,
        SafetyCorner corner,
        int size)
    {
        return corner switch
        {
            SafetyCorner.TopLeft =>
                cursor.X >= bounds.X
                && cursor.X < bounds.X + size
                && cursor.Y >= bounds.Y
                && cursor.Y < bounds.Y + size,
            SafetyCorner.TopRight =>
                cursor.X < bounds.Right
                && cursor.X >= bounds.Right - size
                && cursor.Y >= bounds.Y
                && cursor.Y < bounds.Y + size,
            SafetyCorner.BottomLeft =>
                cursor.X >= bounds.X
                && cursor.X < bounds.X + size
                && cursor.Y < bounds.Bottom
                && cursor.Y >= bounds.Bottom - size,
            SafetyCorner.BottomRight =>
                cursor.X < bounds.Right
                && cursor.X >= bounds.Right - size
                && cursor.Y < bounds.Bottom
                && cursor.Y >= bounds.Bottom - size,
            _ => false
        };
    }
}
