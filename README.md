# Using the repo

## Client tools requirements

- `zmq`
- `tabulate`
- `pick`

## Download

`git clone --recursive  https://github.com/dicarlolab/ThreeDWorld.git`

## Update

`git pull && git submodule update`


# Interface with 3D enviroments

## Queue

At the beginning of our network training programs, we need a way to connect to the environment server and send and receive our messages back and forth. This is done by `ServerTools/tdq_queue.py` which manages all the instances of the ThreeDWorlds as we make. This script will be bound to port number 23402 (by default) on any given machine that we are using to run environments.

To make things more straightforward, use `ClientTools/tdw_client.py` which will auto-connect you to the queue, and allow you to use a small selection of commands to either examine the current processes running on the node, reconnect to an environment process, or create a new process.

## Start a new environment

```python
from tdw_client import TDW_Client
import zmq

# make an instance of a client
tc = TDW_Client('18.93.5.202',
				initial_command='request_create_environment',
				selected_build='TDW-v1.0.0b05.x86_64',  # or skip to select from UI
				get_obj_data=True,
				send_scene_info=True
				)
tc.load_config({
	'environment_scene': 'ProceduralGeneration',
	# other options here
	})
sock = tc.run()  # get a zmq socket for sending and receiving messages
```

### Useful client methods:

	- `load_config`: Loads configuration to the environment. See [Scene configuration options](#scene-configuration-options)

	- `load_profile`

	- `reconnect`: Reconnects you to the port number saved to the client. Returns True if succeeds, returns False if fails.

	- `print_environment_output_log`: Prints the environment's output log to console. NOTE: Not implemented yet.


## Scene configuration options

```python

	{
	"environment_scene" : "ProceduralGeneration",  # THIS MUST BE IN YOUR CONFIG FILE
	"random_seed": 1,  # Omit and it will just choose one at random. Chosen seeds are output into the log(under warning or log level).
	"should_use_standardized_size": False,
	"standardized_size": [1.0, 1.0, 1.0],
	"disabled_items": [],  # ["SQUIRL", "SNAIL", "STEGOSRS"], # A list of item names to not use, e.g. ["lamp", "bed"] would exclude files with the word "lamp" or "bed" in their file path
	"permitted_items": [],  # ["bed1", "sofa_blue", "lamp"],
	"complexity": 7500,
	"num_ceiling_lights": 4,
	"minimum_stacking_base_objects": 15,
	"minimum_objects_to_stack": 100,
	"room_width": 10.0,
	"room_height": 20.0,
	"room_length": 10.0,
	"wall_width": 1.0,
	"door_width": 1.5,
	"door_height": 3.0,
	"window_size_width": 5.0,
	"window_size_height": 5.0,
	"window_placement_height": 5.0,
	"window_spacing": 10.0,  # Average spacing between windows on walls
	"wall_trim_height": 0.5,
	"wall_trim_thickness": 0.01,
	"min_hallway_width": 5.0,
	"number_rooms": 1,
	"max_wall_twists": 3,
	"max_placement_attempts": 300,  # Maximum number of failed placements before we consider a room fully filled.
	"grid_size": 0.4,  # Determines how fine tuned a grid the objects are placed on during Proc. Gen. Smaller the number, the more disorderly objects can look.
	}
```

Okay, so looking through this, we can see that config files are json files. Of special note, we need to observe that the key `'environment_scene'` must be inside the config file, or the unity program will default to making an empty environment and make a complaint in its output log.

The next thing to check out is the random seed which can be used to control the seed deciding random actions in the environment, i.e. where objects get placed and how they get placed.

All the seeds excluding `'environment_scene'`, are all customizable. If you were to write a different environment, you could create a totally different set of keys to expect from the avatar. The base unity code will not care what kind of things you throw into the json config file, so long as you can retrieve them in C# (as a note, make sure this is actually possible as for some special or custom classes, it may actually not be).

## Creating new `enviroment_scene`

There are no requirements for any given Unity scene to be an environment scene type. Even an empty scene will meet the requirements. However, if you want objects in your generated environment you will have to create this in one of two ways:

- METHOD 1:
	You can create fixed scenes by inserting objects using the GUI tool, Unity Editor. Clicking and dragging in objects, and adjusting their transforms can all be done without even writing a single line of code. You can insert scripts wherever needed, but a fixed scene is totally acceptable.

- METHOD 2:
	You can create a scene that is entirely generated. Procedural Generation is a great example of this. The scene contains just a gameobject called Procedural Generation, which runs a script spawning other game objects randomly using data from the config file. You could also make a scene which generates objects in specified locations given information in the config file.

To make these environment scenes, the only requirement is that they be saved under the path `'Assets/Scenes/EnvironmentScenes/<insert scene name>.unity'` in the ThreeDWorld Repo. This way the base scene can locate the scene. When building a new binary to contain your new environment, make sure to check the box labelled with your new scene, or it will not get added to the build.

To build a binary:
 - *File -> Build Settings*
 - select *Standalone*
 - choose *Type: Linux*
 - only check *.x86_64* with none of the check boxes marked

 **Special Note:** Linux binaries must be built on a Mac or Windows system and rsync’ed or an equivalent on to the environment node.

 **IMPORTANT:** please name the builds in the following format: `TDW-v3.2.0b07`, where *b* is for beta which can also be substituted with *a* for alpha. Small bug fixes should increment the beta or alpha counter, big fixes or feature additions should reset the alpha or beta counter and increment the third counter, if major changes are made, increment the second, and use judgement for the first counter. Point of all this is, let's not have duplicate file names lying around in different directories.

Special assets: There is a simple abstract script called SpawnArea. SpawnAreas are used to report locations for Avatars to attempt to spawn. Feel free to write your own extensions of SpawnArea, or use premade prefabs containing SpawnArea extension components. Be sure to save any of the prefab SpawnAreas to the resources folder so the environment can locate and spawn them. (use `Resources.Load<SpawnArea>('Prefabs/<insert name of prefab>')` to acquire prefabs, and `GameObject.Instantiate<SpawnArea>(prefab)` to instantiate them)

The config file can be accessed as a JsonData file under `SimulationManager.argsConfig`. Be sure to import `LitJson.JsonData` to use.


## Sending and receiving messages

When communicating with the environment over `zmq`, you will always send a json with `n` and `msg`. `n` contains your frame expectancy, and `msg` contains your actual message. `msg` will contain an entry `msg_type`, i.e.

```python
	{‘n’ : 4, “msg” : {“msg_type” : “CLIENT_INPUT”, ...}}
```

Here are the available message types and what you can put inside them:

- `CLIENT_INPUT` - for regular frame to frame client input, can do nothing

	vel : [double, double, double] //velocity
	ang_vel : [double, double, double] //angular velocity
	teleport_random : bool //teleport next frame to a new randomly chosen location
	send_scene_info : bool //returns info about the scene
	get_obj_data : bool //returns a list of objects and info concerning them
	relationships : list //currently not being used
	actions : dict //for performing magic actions on objects
		ex. {
			id : str //as given from get_obj_data
			force : [double, double, double]
			torque : [double, double, double]
		    }

- `CLIENT_JOIN` - joining for an environment already up

	N/A

- `CLIENT_JOIN_WITH_CONFIG` - joining and creating a new environment

	config : dict //see config section for what to throw in here

- `SCENE_SWITCH` - creating a new environment, can be of the same kind as before

	config : dict //see config section

- `SCENE_EDIT` - for moving, duplicating, removing, and other kinds of world editing powers (NOTE: not implemented yet)

Beyond that, this is just a simple zmq REQ REP pattern, that starts with your client having 4 frames on queue. Send a message and then get another 4, and repeat.

Each set of four frames contains the following: A header, normals, objects, real image in that order. The header gives you the position, velocity, and of the avatar as well as object info and scene info on request. The images will be received as png’s by default but can be set to be bmp and can be accessed in python via Pillow’s Image class.


# Using Unity

So tragically, some of making scenes requires the use of the GUI. Luckily it isn’t very complex. Essentially to make a new environment scene, you will run File -> New Scene, save it in “Assets/Scenes/EnvironmentScenes”. Once you have an empty scene, the structure of making a scene is to drag and drop prefabs and meshes into the scene editor, or right click on the heirarchy menu and create new objects. Of particular interest, will be to run Create Empty, and to add components to the empty objects. You can attach scripts to the scene in this manner. Special note, these scripts will not be initialized via a constructor! Instead, unity has callback methods called start, awake, update, fixedUpdate, lateUpdate, etc. Start and Awake are used to initialize attributes to the script. The update methods are used as main loops. To see more as to when these methods get called, see the Unity API. Another important feature to objects, is their transforms. Transforms can be adjusted to change position, rotation, and scale. You can check out the Unity API to investigate other components that can be added to objects.

Prefabs, seemingly confusing subject, but surprisingly simple. Prefabs are hierarchies of objects which can be saved outside a scene. If you want two planes to be positioned to bisect each other, you can position them in the scene editor as so, drag one plane into the other plane in the hierarchy menu, and you will wind up creating a single object with sub parts. If you move the outermost object, the sub parts will move with it. You can run methods in a script to acquire information about children or parents in the hierarchy. This hierarchical object can be fairly powerful. The special thing you can do with said object structures in Unity, is that you can save such hierarchies (which can just be one object with no children by the way) as a file called a prefab. The prefab saves all of the information about the hierarchy and can reproduce it in any scene, any number of times.


# License

Apache 2.0