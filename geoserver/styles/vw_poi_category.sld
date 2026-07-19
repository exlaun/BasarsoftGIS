<?xml version="1.0" encoding="UTF-8"?>
<!-- POI markers colored and iconized by category. category_color and category_icon_key come from
     the vw_poi SQL view (the category's own values or the nearest ancestor's). A null color falls
     back to #e11d48; icon_key always falls back to pin. The packaged poi-icons SVGs use white
     strokes/fills so the 16 px glyph remains legible over the 26 px category-colored badge.
     Labels only when zoomed in: MaxScaleDenominator 550000 = ~zoom 10 at EPSG:3857
     (scale denominator = resolution / 0.00028). Client mirror: POI_LABEL_MAX_RESOLUTION = 154 m/px
     (= 550000 x 0.00028) in MapPage.jsx — change both together. -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_poi_category</Name>
    <UserStyle>
      <Title>POIs colored and iconized by category, labeled when zoomed in</Title>
      <FeatureTypeStyle>
        <Rule>
          <Title>Category-colored badge</Title>
          <PointSymbolizer>
            <Graphic>
              <Mark>
                <WellKnownName>circle</WellKnownName>
                <Fill>
                  <CssParameter name="fill">
                    <ogc:Function name="if_then_else">
                      <ogc:Function name="isNull">
                        <ogc:PropertyName>category_color</ogc:PropertyName>
                      </ogc:Function>
                      <ogc:Literal>#e11d48</ogc:Literal>
                      <ogc:PropertyName>category_color</ogc:PropertyName>
                    </ogc:Function>
                  </CssParameter>
                </Fill>
                <Stroke>
                  <CssParameter name="stroke">#ffffff</CssParameter>
                  <CssParameter name="stroke-width">2</CssParameter>
                </Stroke>
              </Mark>
              <Size>26</Size>
            </Graphic>
          </PointSymbolizer>
          <PointSymbolizer>
            <Graphic>
              <ExternalGraphic>
                <OnlineResource
                    xlink:type="simple"
                    xlink:href="poi-icons/${category_icon_key}.svg" />
                <Format>image/svg+xml</Format>
              </ExternalGraphic>
              <Size>16</Size>
            </Graphic>
          </PointSymbolizer>
        </Rule>
        <Rule>
          <Title>Name label at close zoom</Title>
          <MaxScaleDenominator>550000</MaxScaleDenominator>
          <TextSymbolizer>
            <Label>
              <ogc:PropertyName>name</ogc:PropertyName>
            </Label>
            <Font>
              <CssParameter name="font-family">SansSerif</CssParameter>
              <CssParameter name="font-size">12</CssParameter>
              <CssParameter name="font-weight">bold</CssParameter>
            </Font>
            <LabelPlacement>
              <PointPlacement>
                <AnchorPoint>
                  <AnchorPointX>0.5</AnchorPointX>
                  <AnchorPointY>0</AnchorPointY>
                </AnchorPoint>
                <Displacement>
                  <DisplacementX>0</DisplacementX>
                  <DisplacementY>18</DisplacementY>
                </Displacement>
              </PointPlacement>
            </LabelPlacement>
            <Halo>
              <Radius>1.5</Radius>
              <Fill>
                <CssParameter name="fill">#ffffff</CssParameter>
              </Fill>
            </Halo>
            <Fill>
              <CssParameter name="fill">#111827</CssParameter>
            </Fill>
            <VendorOption name="conflictResolution">true</VendorOption>
            <VendorOption name="autoWrap">120</VendorOption>
          </TextSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
