using Microsoft.Xna.Framework;

namespace DataCenterTycoon;

public class DataCenter
{
    public Rectangle Bounds { get; }
    public int Value { get; }
    public float Cost { get; }

    public DataCenter(Rectangle bounds, float cost)
    {
        Bounds = bounds;
        Value = bounds.Width * bounds.Height;
        Cost = cost;
    }
}
