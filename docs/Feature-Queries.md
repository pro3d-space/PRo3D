# Feature: Queries


## Abstract and vision

The goal of this feature is to provide scientists with a tool for quering 3D data from PRo3D in a very flexible way.
This is particularly interesting in combination with the [multitexturing feature](./Feature-Multitexture.md).
In short, given a 3D query (e.g. a polyline annotation), attribute data (such as instrument data layers, or reconstruction layers such as eleveation or area) can be extracted for particular regions. 

While this functionality could be integrated in the UI, currently it extends the PRo3D API which means - by using a REST interface, 
scientists can query 3D data for example in jupyter notebooks and work with the data directly.
There are several variants which have been discussed with science partners. Interesting queries are:
 - all vertices within a polygon annotation
 - all vertices below/above particular height 
 - all vertices within a box defined in geospatial coordinates

Our vision is to extend this queries in an on-demand manner and allow complex combinations. 
Currently we only have simple "inside-polyline" queries and wait for requests from the scientists to extend the API.

## Current design

The main entry point looks like this:

```
POST http://localhost:4321/api/queries/queryAnnotation
Content-Type: application/json

{
	"annotationId": "theId",
	"queryAttributes": [ // what attributes to include in the result, depends on use-case and data, for example: 
		"Ele.aara", // elevation data
		"Are.aara", // area
		...
	]
}
```

The query returns a json object:
```
{
	"filteredPatches": [
		{
			"verticesWorldSpace": [ [x,y,z],... ]  // positions array
			"Ele.ara": [ [ele0], [ele1],.. ] // nan if no value for the vertex
			...
			"indices": [ 0,1,2, 2,3,4 ]  // indices array to construct triangles from the above attributes. only contains triangles with valid positions
		}
	]
}
```


## Implementation status

Almost done, just waiting for feedback of the design to finish the surface level API.


## Demo 

src/Tests contains a jupyter notebooks which shows of quering annotations.

Annotation could be: 

![annotation-for-cutout](https://github.com/pro3d-space/PRo3D/assets/513281/bdaec9f0-2132-40ee-8ddf-0d01043a5a3f)

Later the data can be retrieved using a notebook, also there is a rest entrypoint for retrieving an obj directly:

![cutout](https://github.com/pro3d-space/PRo3D/assets/513281/ede507aa-9722-4b01-b1b0-f3cf665c3a67)

Seealso the entry points: https://github.com/pro3d-space/PRo3D/blob/8fbdabdd1c723d461d43af989c451f7414eef0d8/src/PRo3D.Viewer/RemoteApi.fs#L600

## Caveats

- This feature currently only works with OPC data.
- Currently 3D queries only work for convex polygon annotations. This limitation can be lifted if needed.
