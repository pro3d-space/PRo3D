module GeoJSONTest


        
//[<TestFixture>]
//type TestClass() =
    
//    [<Test>] 
//    member this.PointDeserializeTest() =

//        let geojson = """{"coordinates":[100,0],"type":"Point"}"""
//        let point:GeoJsonGeometry = geojson |> Json.parse |> Json.deserialize

//        Assert.AreEqual(point,GeoJsonGeometry.Point(Coordinate.V2d{X=100.0;Y=0.0}))
    
//    [<Test>] 
//    member this.PointSerializeTest() =
    
//        let point = GeoJsonGeometry.Point(Coordinate.V2d{X=101.0;Y=0.0}) |> Json.serialize |> Json.format
//        let geojson = """{"coordinates":[101,0],"type":"Point"}"""

//        Assert.AreEqual(point,geojson)
    
//    [<Test>] 
//    member this.PolygonDeserializeTest() =

//        let geojson = 
//            """{
//                "type": "Polygon",
//                "coordinates": [
//                    [
//                        [100.0, 0.0],
//                        [101.0, 0.0],
//                        [101.0, 1.0],
//                        [100.0, 1.0],
//                        [100.0, 0.0]
//                    ],
//                    [
//                        [100.8, 0.8],
//                        [100.8, 0.2],
//                        [100.2, 0.2],
//                        [100.2, 0.8],
//                        [100.8, 0.8]
//                    ]
//                ]
//            }"""

//        let polygon = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=100.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=0.0}];
//                                               [Coordinate.V2d{X=100.8;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.8}]])

//        let polygon_des:GeoJsonGeometry = geojson |> Json.parse |> Json.deserialize

//        Assert.AreEqual(polygon,polygon_des)
        
//    [<Test>] 
//    member this.PolygonSerializeTest() =

//        let polygon = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=100.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=0.0};
//                                                Coordinate.V2d{X=101.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=1.0};
//                                                Coordinate.V2d{X=100.0;Y=0.0}];
//                                                [Coordinate.V2d{X=100.8;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.2};
//                                                Coordinate.V2d{X=100.2;Y=0.8};
//                                                Coordinate.V2d{X=100.8;Y=0.8}]])

//        let polygon_ser = polygon |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact
//        let polygon_des:GeoJsonGeometry = polygon_ser |> Json.parse |> Json.deserialize

//        Assert.AreEqual(polygon,polygon_des)


//    [<Test>] 
//        member this.SentinalDatasetTest() =
            
//            let feature = {
//                    geometry = GeoJsonGeometry.Polygon([[Coordinate.V2d{X=13.662865;Y= 47.845915};
//                                                            Coordinate.V2d{X=15.13047; Y=47.853628};
//                                                            Coordinate.V2d{X=15.128057;Y= 46.865628};
//                                                            Coordinate.V2d{X=13.687589;Y= 46.858176};
//                                                            Coordinate.V2d{X=13.662865;Y= 47.845915}]])
//                    bbox = Some([13.662865; 46.858176; 15.13047; 47.853628])
//                }

            
//            let geojson = System.IO.File.ReadAllText "./sentinal.json"

//            let featurecollection_des:GeoJsonFeatureCollection = geojson |> Json.parse |> Json.deserialize

//            Assert.AreEqual(feature,featurecollection_des.features.[1])


