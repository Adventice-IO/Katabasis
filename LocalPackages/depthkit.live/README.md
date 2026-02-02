# Depthkit Live
Copyright 2021 Scatter All Rights reserved.

***

## Quickstart

Add the Keijiro registry to resolve dependencies automatically:

* Under `Project Settings > Package Manager` add a new scoped registry:
	* Name: Keijiro
	* URL: https://registry.npmjs.com
	* Scopes: jp.keijiro

To create a Depthkit Live stream:

Depthkit:
* Startup Depthkit.  Navigate to the Record Tab and make sure your camera is working.  Open `Edit > Preferences` and enable live streaming, note the name of the live stream. 

Unity:
* Copy the generated metadata file from `<your depthkit app project>/_Exports/livestream_meta.txt` into your unity project. 
* Add a depthkit clip + Look to your hierarchy.
* Under the Advanced dropdown on the clip, select `Disable Poster`
* Choose Depthkit Live Player from the player drop down.
* Set the metadata on the clip with the `livestream_meta.txt` file you added from Depthkit. 
* Set the name of your live stream on the `Spout Receiver` component by choosing it from the dropdown.  
* Do not add a render texture.