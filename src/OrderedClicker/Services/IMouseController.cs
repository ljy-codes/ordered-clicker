namespace OrderedClicker.Services;

public interface IMouseController
{
    void MoveTo(int x, int y);

    void LeftClick();

    void EnsureLeftButtonUp();
}
