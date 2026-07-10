<?xml version="1.0" encoding="UTF-8"?>
<!-- Heat map for the vw_heat SQL view via GeoServer's vec:Heatmap rendering transformation.
     The ColorMap below is the 0..1 intensity contract: the legend gradient in
     basarsoft-client/src/pages/MapPage.css (.map-heat-legend-bar) must mirror these
     entries exactly (quantity -> gradient stop %, opacity -> rgba alpha). -->
<StyledLayerDescriptor version="1.0.0"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="http://www.opengis.net/sld http://schemas.opengis.net/sld/1.0.0/StyledLayerDescriptor.xsd">
  <NamedLayer>
    <Name>vw_heat_heatmap</Name>
    <UserStyle>
      <Title>Per-user intensity heat map</Title>
      <FeatureTypeStyle>
        <Transformation>
          <ogc:Function name="vec:Heatmap">
            <ogc:Function name="parameter">
              <ogc:Literal>data</ogc:Literal>
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
