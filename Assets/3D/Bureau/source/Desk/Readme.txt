!!!Attention
Drawer Shelfs have bone therefore, when transferring it to the game engine, you should pay attention to some conditions in order for it to work properly.


--Importing to Unreal Engine--
When importing to unreal engine dont use send to unreal plugin inside blender. Just drag and drop fbx or whatever you prefer, enable Skeletal Mesh and disable the import textures we will transfer the textures later. Now create a new material after that drag and drop textures from the UnrealEngineTextures folder and connect the pins.

--Importing to Unity--
Just drag and drop fbx or whatever you prefer and thats it. Now you need to extract materials and drag and drop textures from the MainTextures folder and select true textures.