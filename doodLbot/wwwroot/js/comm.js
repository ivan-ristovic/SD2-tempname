﻿"use strict"

let connection = new signalR.HubConnectionBuilder().withUrl("/gameHub").build();

let consoleKeyFunc = () => {
    $("#consoleDiv").toggleClass("hiddenConsole");
    event.preventDefault();
}

// CONTROLS => SERVER
function sendUpdateToServer(update) {
    sendToServer("updateGameState", update);
}

// CODE => SERVER
function sendCodeUpdateToServer(code) {
    let toSend = code.elements;
    console.log(JSON.stringify(toSend, null, 2));
    sendToServer("algorithmUpdated", JSON.stringify(toSend));
}

// INIT SERVER => CLIENT
// initialize variables. This is the first server response.
function initClient(data) {
    let alg = data.algorithm;
    console.log("Initial handshake recieved.")
    updateCodeBlocks(alg);
    //console.log(JSON.stringify(alg, null, 2));
    ServerTickrate = data.tickRate;
    MulSpeedsWith = ServerTickrate / 60;
    MapWidth = data.mapWidth;
    MapHeight = data.mapHeight;
    setMapSize(MapWidth, MapHeight);
    console.log("code inventory = ", data.codeInventory);
    console.log("equpimnet inventpry = ", data.equipmentInventory);
}

// starts up connection from clients side
function startConnection() {
    // callbacks for server pushing data to client
    connection.on("StateUpdate", onStateUpdate);
    connection.on("UpdateCodeBlocks", updateCodeBlocks);
    connection.on("InitClient", initClient);

    connection.start().then(function () {
        console.log('connection started');
        sendToServer("ClientIsReady");
    }).catch(function (err) {
        return console.error(err.toString());
    });
}

// GEARSHOP => SERVER
function buyGearServer(name) {
    console.log("sent request to buy gear", name);
    sendToServer("buyGear", name);
}

// listens for the given keyCode
function keyboard(keyCode) {
    let key = {};
    key.code = keyCode;
    key.isDown = false;
    key.isUp = true;
    key.press = undefined;
    key.release = undefined;
    //The `downHandler`
    key.downHandler = event => {
        if (event.keyCode === key.code) {
            //console.log("pressed", key.code);
            UPDATES_FOR_BACKEND.addKeyPress(key.code);
            if (key.isUp && key.press) key.press();
            key.isDown = true;
            key.isUp = false;
            event.preventDefault();
            return;
        }
    };

    //The `upHandler`
    key.upHandler = event => {
        if (event.keyCode === key.code) {
            UPDATES_FOR_BACKEND.addKeyRelease(key.code);
            if (key.isDown && key.release) key.release();
            key.isDown = false;
            key.isUp = true;
            event.preventDefault();
            return;
        }
    };

    //Attach event listeners
    window.addEventListener("keydown", key.downHandler.bind(key), false);
    window.addEventListener("keyup", key.upHandler.bind(key), false);
    return key;
}

// CLIENT => SERVER
let testKeyFunc = () => {
    sendToServer("TestingCallback");
};

// generic function for sending. DRY
function sendToServer(...args)
{
    connection.invoke(...args).catch(function (err) {
        return console.error(err.toString());
    });
}

var timesRecieved = 0;
let left = keyboard(65),
    up = keyboard(87),
    right = keyboard(68),
    down = keyboard(83),
    space = keyboard(32);

let testKey = keyboard(84/*t*/);
testKey.press = testKeyFunc;

let consoleKey = keyboard(192/*~*/);
consoleKey.press = consoleKeyFunc;