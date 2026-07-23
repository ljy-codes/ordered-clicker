using OrderedClicker.Models;

namespace OrderedClicker.Services;

public interface IMouseController
{
    void MoveTo(int x, int y);

    void MoveTo(int x, int y, ScreenBounds virtualScreen)
    {
        MoveTo(x, y);
    }

    void LeftClick();

    void EnsureLeftButtonUp();
}
