<?xml version="1.0" encoding="UTF-8"?>
<!-- Values mirror the vector line style in basarsoft-client/src/pages/MapPage.jsx (makeFeatureStyle):
     stroke width 2, fallback DEFAULT_COLOR #2563eb when color is null. -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_line_color</Name>
    <UserStyle>
      <Title>Per-feature colored lines</Title>
      <FeatureTypeStyle>
        <Rule>
          <LineSymbolizer>
            <Stroke>
              <CssParameter name="stroke">
                <ogc:Function name="if_then_else">
                  <ogc:Function name="isNull">
                    <ogc:PropertyName>color</ogc:PropertyName>
                  </ogc:Function>
                  <ogc:Literal>#2563eb</ogc:Literal>
                  <ogc:PropertyName>color</ogc:PropertyName>
                </ogc:Function>
              </CssParameter>
              <CssParameter name="stroke-width">2</CssParameter>
            </Stroke>
          </LineSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
