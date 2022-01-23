# SimConnect plug-in for ViLa
This repository contains a plugin for the ViLa which enables one to use SimConnect variables as triggers in the ViLa configuration file.

## Installing
Download the zip file and place the folder in the `Plugins` folder in the ViLa directory. After that you can start ViLa. For further instructions you can visit the [ViLa wiki](https://github.com/charliefoxtwo/ViLA/wiki).


## Configuration file

Due to the nature of SimConnect, it's currently necessary typed values are checked by a list of known values, which can be found [here](https://github.com/pieterwasalreadytaken/ViLA-SimConnect-Reader/blob/main/SimConnectReader.SimConnectFSX/TOGGLE_VALUE.cs).

An example configuration file is present in the `SimConnectReaderConfiguration` directory.

You can enter variable names either with underscores or spaces. For further workings of the ViLa configuration file, please visit the [ViLa wiki](https://github.com/charliefoxtwo/ViLA/wiki/Configuration).