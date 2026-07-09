<?xml version="1.0" encoding="UTF-8"?>
<!-- Values mirror the vector point style in basarsoft-client/src/pages/MapPage.jsx (makeFeatureStyle):
     radius 6 => Size 12, white outline 1.5, fallback DEFAULT_COLOR #2563eb when color is null. -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_point_color</Name>
    <UserStyle>
      <Title>Per-feature colored points</Title>
      <FeatureTypeStyle>
        <Rule>
          <PointSymbolizer>
            <Graphic>
              <Mark>
                <WellKnownName>circle</WellKnownName>
                <Fill>
                  <CssParameter name="fill">
                    <ogc:Function name="if_then_else">
                      <ogc:Function name="isNull">
                        <ogc:PropertyName>color</ogc:PropertyName>
                      </ogc:Function>
                      <ogc:Literal>#2563eb</ogc:Literal>
                      <ogc:PropertyName>color</ogc:PropertyName>
                    </ogc:Function>
                  </CssParameter>
                </Fill>
                <Stroke>
                  <CssParameter name="stroke">#ffffff</CssParameter>
                  <CssParameter name="stroke-width">1.5</CssParameter>
                </Stroke>
              </Mark>
              <Size>12</Size>
            </Graphic>
          </PointSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
