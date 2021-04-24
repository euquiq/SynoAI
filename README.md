# SynoAI
A Synology Surveillance Station notification system utilising DeepStack AI, inspired by Christopher Adams' [sssAI](https://github.com/Christofo/sssAI) implementation.

The aim of the solution is to reduce the noise generated by Synology Surveillance Station's motion detection by routing all motion events via a [Deepstack](https://deepstack.cc/) docker image to look for particular objects, e.g. people.

While sssAI is a great solution, it is hamstrung by the Synology notification system to send motion alerts. Due to the delay between fetching the snapshot, processing the image using the AI and requesting the alert, it means that the image attached to the Synology notification is sometimes 5-10 seconds after the motion alert was originally triggered.

SynoAI aims to solve this problem by side-stepping the Synology notifications entirely by allowing other notification systems to be used.

## Buy Me A Coffee! :coffee:

I made this application mostly for myself in order to improve upon Christopher Adams' original idea and don't expect anything in return. However, if you find it useful and would like to buy me a coffee, feel free to do it at [__Buy me a coffee! :coffee:__](https://buymeacoff.ee/djdd87). This is entirely optional, but would be appreciated! Or even better, help supported this project by contributing changes such as expanding the supported notification systems (or even AIs).

## Table of Contents

* [Features](#features)
* [Config](#config)
* [Development Configs](#development-configs)
* [Support AIs](#supported-ais)  
  * [Deepstack](#deepstack)
* [Notifications](#notifications)
  * [Pushbullet](#pushbullet)
  * [Webhook](#webhook)
  * [Telegram](#telegram)
  * [Email](#email) 
  * [HomeAssistant](#homeassistant)
* [Caveats](#caveats)
* [Configuration](#configuration)
  * [1) Configure Deepstack](#1-configure-deepstack)
  * [2) Configure SynoAI](#2-configure-synoai)
  * [3) Create Action Rules](#3-create-action-rules)
  * [Summary](#summary)
* [Updating](#updating)
* [Docker](#docker)
  * [Docker Configuration](#docker-configuration)
  * [Docker Compose](#docker-compose)
* [Example appsettings.json](#example-appsettingsjson)
* [Problems/Debugging](#problemsdebugging)
  * [Logging](#logging)
  * [Trouble Shooting](#trouble-shooting)

## Features
* Triggered via an Action Rule from Synology Surveillance Station
* Works using the camera name and requires no technical knowledge of the Surveillance Station API in order to retrieve the unique camera ID
* Uses an AI for object/person detection
* Produces an output image with highlighted objects using the original image at the point of motion detection
* Sends notification(s) at the point of notification with the processed image attached
* The AI does not need to run on the Synology box and can be run on an another server.

## Config

An example appsettings.json configuration file can be found [here](#example-appsettingsjson) and all configuration for notifications and AI can be found under their respective sections. The following are the top level configs for communication with Synology Surveillance Station:

* Url [required]: The URL and port of your NAS, e.g. http://{IP}:{Port}
* User [required]: The user that will be used to request API snapshots
* Password [required]: The password of the user above
* AllowInsecureUrl [optional] (Default ```false```): Whether to allow an insecure HTTPS connection to the Synology API
* Cameras [required]: An array of camera objects
  * Name: [required]: The name of the camera on Surveillance Station
  * Types: [required]: An array of types that will trigger a notification when detected by the AI, e.g. ["Person", "Car"]
  * Threshold [required]: An integer denoting the required confidence of the AI to trigger the notification, e.g. 40 means that the AI must be 40% sure that the object detected was a person before SynoAI sends a notification
  * MinSizeX [optional] (Default: ```NULL```): The minimum pixels that the object must be horizontally to trigger a change (will override the default set on the top level MinSizeX)
  * MinSizeY [optional] (Default: ```NULL```): The minimum pixels that the object must be vertically to trigger a change (will override the default set on the top level MinSizeY).
* Notifiers [required]: See [notifications](#notifications)
* Quality [optional] (Default: ```Balanced```): The quality, aka "profile type" to use when taking a snapshot. This will be based upon the settings of the streams you have configured in Surveillance Station. i.e. if your low, balanced and high streams have the same settings in Surveillance Station, then this setting will make no difference. But if you have a high quality 4k stream, a balance 1080p stream and a low 720p stream, then setting to high will return and process a 4k image. Note that the higher quality the snapshot, the longer the notification will take. Additionally, the larger the image, the smaller your detected objects may be, so ensure you set the MinSizeX/MinSizeY values respectively.
  * High: Takes the snapshot using the profile type "High quality"
  * Balanced: Takes the snapshot using the profile type "Balanced"
  * Low: Takes the snapshot using the profile type "Low bandwidth" 
* MinSizeX [optional] (Default: ```50```): The minimum size in pixels that the object must be to trigger a change (will be ignored if specified on the Camera)
* MinSizeY [optional] (Default: ```50```): The minimum size in pixels that the object must be to trigger a change (will be ignored if specified on the Camera).
* Delay [optiona] (Default: ```5000```): The period of time in milliseconds (ms) that must occur between the last motion detection of camera and the next time it'll be processed. i.e. if your delay is set to 5000 and your camera reports motion 4 seconds after it had already reported motion to SynoAI, then the check will be ignored. However, if the report from Surveillance Station is more than 5000ms, then the cameras image will be processed.
* DrawMode [optional] (Default: ```Matches```): Whether to draw all predictions from the AI on the capture image:
  * Matches: Will draw boundary boxes over any object/person that matches the types defined on the cameras
  * All: Will draw boundary boxes over any object/person that the AI detected
  * Off: Will not draw boundary boxes (note - this will speed up time between detection and notification as SynoAI will not have to manipulate the image)
* BoxColor [optiona] (Default: ```#FF0000```): The colour of the border of the boundary box
* Font [optiona] (Default: ```Tahoma```): The font to use when labelling the boundary boxes on the output image
* FontSize [optiona] (Default: ```12```): The size of the font to use (in pixels) when labelling the boundary boxes on the output image
* FontColor [optiona] (Default: ```#FF0000```): The colour of the text for the labels when labelling the boundary boxes on the output image
* TextOffsetX [optional] (Default: ```2```) : The number of pixels to offset the label from the left of the inside of the boundary image on the output image
* TextOffsetY [optional] (Default: ```2```) : The number of pixels to offset the label from the top of the inside of the boundary image on the output image
* SaveOriginalSnapshot [optional] (Default: ```false```): Whether to save the source snapshot that was captured from the API before it was sent to and processed by the AI.

## Development Configs
Configs which should be changed for debugging (change at own risk):
* ApiVersionInfo [optiona] (Default: ```6```): The API version to use for the SYNO.API.Info API. According to the function spec for DSM, 6 is the correct version for DSM 6+
* ApiVersionCamera [optional] (Default: ```9```): The API version to use for SYNO.SurveillanceStation.Camera. According to the functional spec for DSM, 9 is the correct version for SSS 8+.

## Supported AIs
* [Deepstack](https://deepstack.cc/)

In order to specify the AI to use, set the Type property against the AI section in the config:

```json
"AI": {
  "Type": "DeepStack"
}
```

### Deepstack

The Deepstack API is a free to use AI that can identify objects, faces and more. Currently SynoAI makes use of the object detection model, which allows detection of people, cars, bicycles, trucks and even giraffes! For a full list of supported types see the [Deepstack documentation](https://docs.deepstack.cc/object-detection/#classes).

```json
"AI": {
  "Type": "DeepStack",
  "Url": "http://10.0.0.10:83"
}
```
* Url [required]: The URL of the AI to POST the image to

## Notifications

Multiple notifications can be triggered when an object is detected. Each notification will have a defined "Type" and the sections below explain how each notification should be defined. An optional feature allows notifications to be triggered only by specified cameras.

```json
"Notifiers": [
  {
    "Type": "{Type}",
    "Cameras": [ "Driveway", "Garden"]
  }
]
```

* Type [required]: One of the supported notification types (see each type for additional required and optional configs below)
* Cameras [optional]: A list of camera names that the notification will be triggered for. Allows certain notifications to only trigger for specific cameras; not specifying the value, or setting cameras to an empty array, will result in the notification being sent for all cameras.  

### Pushbullet
The [Pushbullet](https://www.pushbullet.com/) notification will send an image and a message containing a list of detected object types. An API key will need to be obtained from your Pushbullet account. Currently the notification will be sent to all devices that the API key belongs to.

```json
{
  "Type": "Pushbullet",
  "ApiKey": "0.123456789"
}
```
* ApiKey [required]: The API key for the Pushbullet service

### Webhook 
The webhook notification will POST an image to the specified URL with a specified field name.

```json
{
  "Url": "http://servername/resource",
  "Method": "POST",
  "Field": "image",
  "SendImage": true,
  "SendTypes": false
}
```
* Url [required]: The URL to send the image to
* Method [optional] (Default: ```POST```): The HTTP method to use:
  * GET
  * POST
  * PATCH
  * PUT
  * DELETE
* Authorization [optional] (Default: ```None```): The type of Authorization to use if any
  * Basic
    * Username [optional]: The username to use when using Basic Authorization
    * Password [optional]: The password to use when using Basic Authorization
  * Bearer
    * Token [optional]: The token to use when using Basic Authorization
* Field [optional] (Default: ```image```): The field name of the image in the POST data
* SendImage [optional] (Default: ```true```): The image will be sent to the webhook when the method is POST, PATCH or PUT
* SendTypes [optional] (Default: ```false```): The list of found types will be sent to the webhook in the body of the request as a JSON string array.

### Telegram
The telegram bot will send notifications and images when motion has been detected. To use this notification, you will need to set up your own Telegram bot using [one](https://core.telegram.org/bots#6-botfather) of the many [guides](https://docs.microsoft.com/en-us/azure/bot-service/bot-service-channel-connect-telegram?view=azure-bot-service-4.0) available.

```json
{
  "Type": "Telegram",
  "ChatID": "000000000",
  "Token": "",
  "PhotoBaseURL": ""
}
```

* Chat ID [required]: The ID of the chat with your bot. There are a number of ways of retrieving your ID, including [this one](https://sean-bradley.medium.com/get-telegram-chat-id-80b575520659).
* Token [required]: The API token provided to you by Telegram's BotFather when creating your bot
* PhotoBaseURL [optional]: Should only be filled in if you're using the Synology Web Station and can self host your images. If left blank, the file will be uploaded to Telegram for you. However, if you exposed your Captures directory on Web Station, this would be the URL to your captures folder.

### Email 
The email notification will send and email with the attached image to the specified recipient.

```json
{
  "Type": "Email",
  "Destination": "youremail@example.com",
  "Host": "smtp.server.com",
  "Port": 465,
  "Username": "username",
  "Password": "password",
  "Encryption": "Auto"
}
```

* Destination [required]: The email recipient for SynoAi notifications
* Host [required]: The hostname of the SMTP server e.g. "smtp.gmail.com"
* Port [optional] (Default: 25): The port used by the SMTP server e.g 25, 465, 587
* Username [optional] (Default: ``````):  The email used to authenticate with the smtp server
* Password [optional] (Default: ``````) : The password used to authenticate with the smtp server
* Encryption [optional] (Default: ```None```):  The encryption method used by the host:
  * None: No SSL or TLS encryption should be used
  * Auto: Allow SynoAI to decide which SSL or TLS options to use. If the server does not support SSL or TLS, then the connection will continue without any encryption
  * SSL: The connection should use SSL or TLS encryption immediately
  * STARTTLS: Elevates the connection to use TLS encryption immediately after reading the greeting and capabilities of the server. If the server does not support the STARTTLS extension, then the connection will fail and a NotSupportedException will be thrown
  * STARTTLSWHENAVAILABLE: Elevates the connection to use TLS encryption immediately after reading the greeting and capabilities of the server, but only if the server supports the STARTTLS extension.

#### Gmail
Note that to send email using GMail you may need to log into your Google Account (or Admin Console) and allow "[Less secure app access](https://support.google.com/accounts/answer/6010255)".

```json
{
  "Type": "Email",
  "Destination": "youremail@gmail.com",
  "Host": "smtp.gmail.com",
  "Port": 587,
  "Encryption": "StartTLS",
  "Username": "youremail@gmail.com",
  "Password": "yourpassword"
},
```

### HomeAssistant
Integration with HomeAssistant can be achieved using the [Push](https://www.home-assistant.io/integrations/push/) integration and by calling the HomeAssistant webhook with the SynoAI Webhook notification.

Example HomeAssistant Configuration.yaml:
``` yaml
camera:
  - platform: push
    name: Motion Driveway
    webhook_id: motion_driveway
    timeout: 1
    buffer: 1
```

HomeAssistant requires the POSTed image field to be called "image" (which is the default for SynoAI), so from the SynoAI side the integration is simply a case of creating a Webhook and pointing it at your HomeAssistant's IP and Webhook ID:

``` json
{
  "Type": "Webhook",
  "Url": "http://nas-ip:8123/api/webhook/motion_driveway"
}	
```

Automations can be created using this webhook by checking for changes for the camera entity state. When the Push camera is not receiving any data, it will be in the state of "Idle". When the state switches to "Recording", it means that the webhook has started receiving data. For the fastest automation responses, perform your actions immediately on that state change.

Multiple webhooks can be set up, each pointed at a different HomeAssistant Push camera. Additionally, you can create an automation that is triggered on a Webhook call. Then just use the SynoAI webhook notification to call that webhook. Note that it's wasteful to send an image when triggering the non-Push webhooks on HomeAssistant, so ensure that SendImage is set to false.

## Caveats
* SynoAI still relies on Surveillance Station triggering the motion alerts
* Looking for an object, such as a car on a driveway, will continually trigger alerts if that object is in view of the camera when Surveillance Station detects movement, e.g. a tree blowing in the wind.

## Configuration
The configuration instructions below are primarily aimed at running SynoAI in a docker container on DSM (Synology's operating system). Docker will be required anyway as Deepstack is assumed to be setup inside a Docker container. It is entirely possible to run SynoAI on a webserver instead, or to install it on a Docker instance that's not running on your Synology NAS, however that is outside the scope of these instructions. Additionally, the configuration of the third party notification systems (e.g. generating a Pushbullet API Key) is outside the scope of these instructions and can be found on the respective applications help guides.

The top level steps are:
* Setup the Deepstack Docker image on DSM
* Setup the SynoAI image on DSM
* Add Action Rules to Synology Surveillance Station's motion alerts in order to trigger the SynoAI API.

### 1) Configure Deepstack
The following instructions explain how to set up the Deepstack image using the Docker app built into DSM. 

* Download the deepquestai/deepstack:latest image
* Run the image
* Enter a name for the image, e.g. deepstack
* Edit the advanced settings
* Enable auto restarts
* On the port settings tab;
  * Enter a port mapping to port 5000 from an available port on your NAS, e.g. 83
* On the Environment tab;
  * Set MODE: Low
  * Set VISION-DETECTION: True
* Accept the advanced settings and then run the image
* Open a web browser and go to the Deepstack page by navigating to http://{YourIP}:{YourDeepstackPort}
* If you've set everything up successfully and you're using the latest version of DeepStack, then you'll see a message saying "DeepStack Activated"
   
### 2) Configure SynoAI
The following instructions explain how to set up the SynoAI image using the Docker app built into DSM. For docker-compose, see the example file in the src, or in the documentation below.

* Create a folder called synoai (this will contain your Captures directory and appsettings.json)
* Put your appsettings.json file in the folder
* Create a folder called Captures 
* Open Docker in DSM
* Download the djdd87/synoai:latest image by either;
  * Searching the registry for djdd87/synoai
  * Going to the image tab and;
    * Add > Add from URL
    * Enter https://hub.docker.com/r/djdd87/synoai
* Run the image
* Enter a name for the image, e.g. synoai
* Edit the advanced settings
* Enable auto restarts
* On the volumes tab;
   * Add a file mapping from your appsettings.json to /app/appsettings.json
   * Add a folder mapping from your captures directory to /app/Captures (optional)
* On the port settings tab;
   * Enter a port mapping to port 80 from an available port on your NAS, e.g. 8080

### 3) Create Action Rules
The next step is to configure actions inside Surveillance Station that will call the SynoAI API. 

* Open up Surveillance Station
* Open Action Rules
* Create a new rule and enter;
  * Name: A name for the action e.g. Trigger SynoAI - Driveway
  * Rule type: Triggered (Default)
  * Action type: Interruptible (Default)
* Click next to open the Event tab and enter;
  * Event source: Camera
  * Device: Your camera, e.g. Driveway
  * Event: Motion Detected
* Click next to open the Action tab and enter;
  * Action device: Webhook
  * URL: http://{YourIP}:{YourPort}/Camera/{CameraName}, e.g. http://10.0.0.10:8080/Camera/Driveway, where
    * YourIP: Is the IP of your NAS, or the Docker server where SynoAI is deployed
    * YourPort: The port that the SynoAI image is listening on as you configured above.
    * CameraName: The name of the camera, e.g. Driveway
  * Username: Blank
  * Password: Blank
  * Method: GET
  * Times: 1
* Click test send and if everything is set up correctly, then you'll get a green tick
* Click next and the action will now be created.

### Summary

Congratulations, you should now have a trigger calling the SynoAI API for your camera every time Surveillance Station detects motion. In order to set up multiple cameras, just create a new Action Rule for each camera.

Note that SynoAI is still reliant on Surveillance Station detecting the motion, so this will need some tuning on your part. However, it's now possible to up the sensitivity and avoid false-positives as SynoAI will only notify you (via your preferred notification system/app) when an object is detected, e.g. a Person.

## Updating

If you have followed the above instructions to setup the container using the DSM UI, then the following instructions are how to correctly update:

* Pull the :latest image down again from the Image tab
* Wait until DSM shows an alert to say that djdd87/synoai has finished downloading
* Stop your SynoAI container
* Select the container and go to Actions > Clear
* Start the container.

## Docker
SynoAI can be installed as a docker image, which is [available from DockerHub](https://hub.docker.com/r/djdd87/synoai).

### Images
The following is a list of the available images and their meaning:
 
* Latest: The latest version
* Stable: A known stable version, which will only periodically receive updates (ensure the readme on the stable branch is followed as the latest branch's readme may contain changes that are not relevant to the stable image).

### Docker Configuration
The image can be pulled using the Docker cli by calling:
```
docker pull djdd87/synoai:latest
```
To run the image a volume must be specified to map your appsettings.json file. Additionally a port needs mapping to port 80 in order to trigger the API. Optionally, the Captures directory can also be mapped to easily expose all the images output from SynoAI.

```
docker run 
  -v /path/appsettings.json:/app/appsettings.json 
  -v /path/captures:/app/Captures 
  -p 8080:80 djdd87/synoai:latest
```

### Docker-Compose
```yaml
version: '3.4'

services:
  synoai:
    image: djdd87/synoai:latest
    build:
      context: .
      dockerfile: ./Dockerfile
    ports:
      - "8080:80"
    volumes:
      - /docker/synoai/captures:/app/Captures
      - /docker/synoai/appsettings.json:/app/appsettings.json
```

## Example appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Warning"
    }
  },

  "Url": "http://10.0.0.10:5000",
  "User": "SynologyUser",
  "Password": "SynologyPassword",

  "MinSizeX": 100,
  "MinSizeY": 100,
  
  "AI": {
    "Type": "DeepStack",
    "Url": "http://10.0.0.10:83"
  },

  "Notifiers": [
    {
      "Type": "Pushbullet",
      "ApiKey": "0.123456789"
    },
    {
      "Type": "Webhook",
      "Url": "http://server/images",
      "Method": "POST",
      "Field": "image"
    }
  ],

  "Cameras": [
    {
      "Name": "Driveway",
      "Types": [ "Person", "Car" ],
      "Threshold": 45,
      "MinSizeX": 250,
      "MinSizeY": 500
    },
    {
      "Name": "BackDoor",
      "Types": [ "Person" ],
      "Threshold": 30
    }
  ]
}
```

## Problems/Debugging

### Logging
If issues are encountered, to get more verbose information in the logs, change the logging to the following:

```json  
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  }
}
```
This will output the full information log and help identify where things are going wrong, as well as displaying the confidence percentages from Deepstack.

### Trouble Shooting

#### "Failed due to Synology API error code X"
* 400 Invalid password.
  * If your password is definitely correct and you are still getting a 400 error code, then there's potentially an issue with the Synology DSM user configuration. However, if you cannot see any issues with the permissions, try creating a new user from SurveillanceStation directly.
* 401 Guest or disabled account.
* 402 Permission denied.
* 403 One time password not specified.
  * You have Two-Factor Authentication enabled and this is not currently support. Please create a dedicated user account without 2FA limited to just SurveillanceStation.
* 404 One time password authenticate failed.
* 405 App portal incorrect.
* 406 OTP code enforced.
* 407 Max Tries (if auto blocking is set to true).
* 408 Password Expired Can not Change.
* 409 Password Expired.
* 410 Password must change (when first time use or after reset password by admin).
* 411 Account Locked (when account max try exceed).

#### Common Synology Error Codes
* 100: Unknown error
* 101: Invalid parameters
* 102: API does not exist
* 103: Method does not exist
* 104: This API version is not supported
* 105: Insufficient user privilege
  * If this occurs, check your username and password, or;
  * Try creating a specific user for Synology Surveillance Station, or;
  * Ensure your user has permission to the Surveillance Station application
* 106: Connection time out
* 107: Multiple login detected
