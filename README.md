# mmdiva

A tool to port MMD motions (dance and camera) into Project Diva Megamix+

It generates these files:
- auth_3d/CAMPV___.farc, auth_3d/mod_auth_3d_db.bin
- rob/mot_PV___.farc
- script/pv_____normal.dsc (just an empty base dsc that sets a camera)

Usage example:

> mmdiva --dance-vmd \<MMD VMD dance file\> --pmx \<MMD PMX model file\> --megamix \<Root folder for MegaMix+\> --pv \<PV001 or something like that\> --output \<Output directory for the motion FARC file\> --camera-vmd \<MMD VMD camera file\> --length-seconds \<song length in seconds\> --db-uid \<an unique id to be used in the mod db files\>

Used references: 
* [saba](https://github.com/benikabocha/saba/tree/master) for MMD Pose stuff
* [ReDiva](https://github.com/korenkonder/ReDIVA) for Project Diva Rob stuff
* [MikuMikuLibrary](https://github.com/blueskythlikesclouds/MikuMikuLibrary) based tool
* [dsceditor](https://github.com/nastys/nastys.github.io/tree/master/dsceditor) for DSC opcodes (and well I guess my own [thediva](https://github.com/alurafes/thediva))