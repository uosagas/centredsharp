namespace CentrED;

public static class Constants
{
    // 1 / Math.Sqrt(2)
    public const float RSQRT2 = 0.70710678118654752440084436210485f;
    public const float TILE_SIZE = 44 * RSQRT2;
    public const float TILE_Z_SCALE = 4.0f;

    // Default data directory of the UO-Sagas client installer.
    public const string DEFAULT_CLIENT_PATH =
        @"C:\Program Files\Mystic Forge Studios UG (haftungsbeschränkt)\Ultima Online - Sagas\UOData";
}