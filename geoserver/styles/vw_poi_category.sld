<?xml version="1.0" encoding="UTF-8"?>
<!-- POI markers colored by category. category_color comes from the vw_poi SQL view (the category's
     own color or the nearest ancestor's); when the whole chain is null the fallback #e11d48 matches
     POI_COLOR in basarsoft-client/src/pages/MapPage.jsx. Marker mirrors the client POI style:
     radius 7 => Size 14, white outline 2.
     Labels only when zoomed in: MaxScaleDenominator 50000 = ~zoom 13.5 at EPSG:3857
     (scale denominator = resolution / 0.00028). Client mirror: POI_LABEL_MAX_RESOLUTION = 14 m/px
     (= 50000 x 0.00028) in MapPage.jsx — change both together. -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_poi_category</Name>
    <UserStyle>
      <Title>POIs colored by category, labeled when zoomed in</Title>
      <FeatureTypeStyle>
        <Rule>
          <Title>Category-colored marker</Title>
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
              <Size>14</Size>
            </Graphic>
          </PointSymbolizer>
        </Rule>
        <Rule>
          <Title>Name label at close zoom</Title>
          <MaxScaleDenominator>50000</MaxScaleDenominator>
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
                  <DisplacementY>10</DisplacementY>
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
