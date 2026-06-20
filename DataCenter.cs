using Microsoft.Xna.Framework;

namespace DataCenterTycoon;

public class DataCenter
{
    public Rectangle Bounds { get; }
    public float Cost { get; }

    public DataCenter(Rectangle bounds, float cost)
    {
        Bounds = bounds;
        Cost = cost;
    }
}
