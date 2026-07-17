<?xml version="1.0" encoding="UTF-8"?>
<!-- Weighted heat map for the vw_konum SQL view (Konum Analizi). Identical to vw_heat_heatmap.sld
     plus the weightAttr parameter: each POI contributes its criterion's weight (1..100) to the kernel
     density instead of a flat 1, so high-weight categories dominate the hotspots. The output surface
     is normalized to 0..1 against the max cell, exactly like vw_heat — the client legend gradient
     (.map-heat-legend-bar in MapPage.css) mirrors this ColorMap 1:1 and is reused as-is. -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_konum_heatmap</Name>
    <UserStyle>
      <Title>Weighted location-analysis heat map</Title>
      <FeatureTypeStyle>
        <Transformation>
          <ogc:Function name="vec:Heatmap">
            <ogc:Function name="parameter">
              <ogc:Literal>data</ogc:Literal>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>weightAttr</ogc:Literal>
              <ogc:Literal>weight</ogc:Literal>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>radiusPixels</ogc:Literal>
              <ogc:Literal>35</ogc:Literal>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>pixelsPerCell</ogc:Literal>
              <ogc:Literal>10</ogc:Literal>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>outputBBOX</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_bbox</ogc:Literal>
              </ogc:Function>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>outputWidth</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_width</ogc:Literal>
              </ogc:Function>
            </ogc:Function>
            <ogc:Function name="parameter">
              <ogc:Literal>outputHeight</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_height</ogc:Literal>
              </ogc:Function>
            </ogc:Function>
          </ogc:Function>
        </Transformation>
        <Rule>
          <RasterSymbolizer>
            <Geometry>
              <ogc:PropertyName>geom</ogc:PropertyName>
            </Geometry>
            <Opacity>0.8</Opacity>
            <ColorMap type="ramp">
              <ColorMapEntry color="#3b82f6" quantity="0.00" opacity="0"/>
              <ColorMapEntry color="#3b82f6" quantity="0.15" opacity="0.35"/>
              <ColorMapEntry color="#22d3ee" quantity="0.35" opacity="0.55"/>
              <ColorMapEntry color="#22c55e" quantity="0.55" opacity="0.70"/>
              <ColorMapEntry color="#facc15" quantity="0.75" opacity="0.85"/>
              <ColorMapEntry color="#ef4444" quantity="1.00" opacity="0.95"/>
            </ColorMap>
          </RasterSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
