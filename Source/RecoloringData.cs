using KSPShaderTools;

namespace LazyPainter
{
    public static class RecoloringDataExtensions
    {
        public static bool IsEqual(this RecoloringData colour1, RecoloringData colour2)
        {
            return colour1.color == colour2.color &&
                   colour1.metallic == colour2.metallic &&
                   colour1.specular == colour2.specular &&
                   colour1.detail == colour2.detail;
        }
    }
}
