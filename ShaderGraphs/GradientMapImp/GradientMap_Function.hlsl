//#pragma multi_compile __ MODE_LUMINANCE MODE_GREYSCALE MODE_ERROR
//#define MODE_LUMINANCE

float CalculateValue(float4 col);

#if defined(_MODE_LUMINANCE) && defined(_MODE_GREYSCALE)
    #error "MODE_LUMINANCE and MODE_GREYSCALE cannot both be active!"
#else
    #ifdef _MODE_LUMINANCE
        #define LUMINOSITY_RED_WEIGHT           0.2126f
        #define LUMINOSITY_GREEN_WEIGHT         0.7152f
        #define LUMINOSITY_BLUE_WEIGHT          0.0722f

        float CalculateValue(float4 col)
        {
            return col.r * LUMINOSITY_RED_WEIGHT + col.g * LUMINOSITY_GREEN_WEIGHT + col.b * LUMINOSITY_BLUE_WEIGHT;
        }
    #else
        #ifdef _MODE_GREYSCALE

            float CalculateValue(float4 col)
            {
                return (col.r + col.g  + col.b) / 3;
            }
        #else
            float CalculateValue(float4 col)
            {
                return 0; //error
            }
        #endif
    #endif
#endif

void CalculateBW_float(float4 col, out float4 outCol)
{
        float val =
#ifdef _MODE_ERROR
            1;
#else
            CalculateValue(col);
#endif
            
        outCol = float4(val, val, val, col.a);
}
