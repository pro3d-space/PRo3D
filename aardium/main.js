const electron = require('electron')
const os = require('os')
const fs = require('fs')
const proc = require('child_process');

// Module to control application life.
const app = electron.app
// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow
const electronLocalshortcut = require('electron-localshortcut');
const screen = electron.screen

//app.allowRendererProcessReuse = false;



const path = require('path')
const url = require('url')
const getopt = require('node-getopt')
const ws = require("nodejs-websocket")

const options =
[
  ['w' , 'width=ARG'              , 'initial window width'],
  ['h' , 'height=ARG'             , 'initial window height'],
  ['u' , 'url=ARG'                , 'initial url' ],
  ['g' , 'debug'                  , 'show debug tools'],
  ['i' , 'icon=ARG'               , 'icon file'],
  ['t' , 'title=ARG'              , 'window title'],
  ['m' , 'menu'                   , 'display default menu'],
  ['d' , 'hideDock'               , 'hides dock toolback on mac'],
  ['a' , 'autoclose'              , 'autoclose on main window close'],
  [''  , 'fullscreen'             , 'display fullscreen window'],
  ['e' , 'experimental'           , 'enable experimental webkit extensions' ],
  [''  , 'frameless'              , 'frameless window'],
  [''  , 'woptions=ARG'           , 'BrowserWindow options'],
  [''  , 'server=port'            , 'run server for offscreen rendering' ]
];
// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow

var hideDock = false;

function createWindow (url, done) {

  var plat = os.platform();
  var defaultIcon = "aardvark.ico";
  console.warn(plat);
  if(plat == 'linux') defaultIcon = "aardvark.png";
  else if(plat == 'darwin') defaultIcon = "aardvark_128.png";

  var argv = process.argv;
  if(!argv) argv = [];

  var res = getopt.create(options).bindHelp().parse(argv); 
  var preventTitleChange = true;
  var opt = res.options;
  if(!opt.width) opt.width = 1024;
  if(!opt.height) opt.height = 768;
  
  opt.url = url;
  
  if(!opt.icon) opt.icon = path.join(__dirname, defaultIcon);
  if(!opt.title) {
    opt.title = "Aardvark rocks \\o/";
    preventTitleChange = false;
  }
  if(opt.hideDock && plat == 'darwin'){
    hideDock = true;
  }
  
  if(!opt.experimental) opt.experimental = false;
  if(!opt.frameless) opt.frameless = false;

  const defaultOptions =
    {
      width: parseInt(opt.width),
      height: parseInt(opt.height),
      title: opt.title,
      icon: opt.icon,
      fullscreen: opt.fullscreen,
      fullscreenable: true,
      frame: !opt.frameless,
      webPreferences: { 
        nodeIntegration: false, 
        contextIsolation: false,
        nativeWindowOpen: true,
        enableRemoteModule: true,
        experimentalFeatures: opt.experimental,
          webSecurity: true, 
          devTools: true,
        preload: path.join(__dirname, 'preload.js')
      }
    };

  const windowOptions = 
    opt.woptions ? Object.assign({}, defaultOptions, JSON.parse(opt.woptions)) : defaultOptions;

  // Create the browser window.
  mainWindow = new BrowserWindow(windowOptions);

  electron.app.on('browser-window-created',function(e,window) {
      window.setMenu(null);
      window.setTitle(opt.title);
      window.on('page-title-updated', (e,c) => {
        e.preventDefault();
      });
  });
  
  if(hideDock) {
    electron.app.dock.hide();
    if(opt.autoclose) mainWindow.on('closed', () => electron.app.quit());
  }


  if(!opt.menu) mainWindow.setMenu(null);
  if(plat == "darwin") {
    electron.app.dock.setIcon(opt.icon);
  }
  // if(process.argv.length > 2) url = process.argv[2];
  if(preventTitleChange) {
    mainWindow.on('page-title-updated', (e,c) => {
      e.preventDefault();
    });
  }

  electronLocalshortcut.register(mainWindow,'F11',() => {
    var n = !mainWindow.isFullScreen();
    console.log("fullscreen: " + n);
    mainWindow.setFullScreen(n);
  });
  if(opt.debug) {
    
    electronLocalshortcut.register(mainWindow,'F10',() => {
      console.log("devtools");
      mainWindow.webContents.toggleDevTools();
    });

    electronLocalshortcut.register(mainWindow,'F5',() => {
      console.log("reload");
      mainWindow.webContents.reload(true);
    });

	

  }
  
    // and load the index.html of the app.
  mainWindow.loadURL(opt.url).then(v => {
    done();
  });

  // and load the index.html of the app.
  mainWindow.loadURL(opt.url);

  // Open the DevTools.
  // mainWindow.webContents.openDevTools()

  // Emitted when the window is closed.
  mainWindow.on('closed', function () {
    // Dereference the window object, usually you would store windows
    // in an array if your app supports multi windows, this is the time
    // when you should delete the corresponding element.
    mainWindow = null
  })
}

function runOffscreenServer(port) {
    // process gets killed otherwise
    const dummyWin = new BrowserWindow({ show: false, webPreferences: { offscreen: true, contextIsolation: false } })

    const server =
        ws.createServer(function (conn) {
            console.log("client connected");

            let win = null;
            let mapping = null;
            let arr = null;
            let connected = true;
            let offset = 0;
            let lastOffset = -1;

            function append(data) {
                const oldOffset = offset;
                const e = offset + data.byteLength;
                if (e <= arr.byteLength) {
                    arr.set(data, oldOffset);
                    lastOffset = oldOffset;
                    offset = e;
                    return oldOffset
                }
                else {
                    arr.set(data, 0)
                    lastOffset = 0;
                    offset = data.byteLength;
                    return 0;
                }
            }

            function close() {
                if (connected) {
                    connected = false;
                    console.log("client disconnected");
                    if (win) try { win.close(); } catch {}
                    if (mapping) try { mapping.close(); } catch { }
                    mapping = null;
                    win = null
                }
            }

            function command(cmd) {
                switch (cmd.command) {
                    case "init":
                        if (mapping) mapping.close();
                        if (win) win.close();

                        mapping = new SharedMemory(cmd.mapName, cmd.mapSize);
                        win =
                            new BrowserWindow({
                                titleBarStyle: "hidden",
                                backgroundThrottling: false,
                                frame: false,
                                useContentSize: true,
                                show: false,
                                transparent: true,
                                webPreferences: { offscreen: true, devTools: true, contextIsolation: false },
                                width: cmd.width,
                                height: cmd.height
                            })
                        arr = new Uint8Array(mapping.buffer, 0);
                       

                        win.setContentSize(cmd.width, cmd.height);
                        win.loadURL(cmd.url);
                        win.webContents.setFrameRate(60.0);

                        conn.send(JSON.stringify({ type: "initComplete" }));

                        win.webContents.on('cursor-changed', (e, typ) => {
                            if (!connected) return;
                            conn.send(JSON.stringify({ type: "changecursor", name: typ }));
                        });

                        win.focus();
                        const partialFrames = cmd.incremental || false;

                        win.webContents.on('paint', (event, dirty, image) => {
                            if (!connected) return;
                            const size = image.getSize();
                            if (partialFrames && dirty.width < size.width && dirty.height < size.height && lastOffset >= 0) {
                                const part = image.crop(dirty);
                                const bmp = part.toBitmap();
                                const partSize = part.getSize();

                                // update affected part in last frame
                                let srcIndex = 0
                                let dstIndex = lastOffset + 4 * (dirty.x + size.width * dirty.y);
                                const jy = 4 * (size.width - dirty.width)
                                for (y = 0; y < partSize.height; y++) {
                                    for (x = 0; x < partSize.width; x++) {
                                        // BGRA
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                    }
                                    dstIndex += jy;
                                }

                                conn.send(
                                    JSON.stringify({
                                        type: "partialframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: lastOffset,
                                        byteLength: 0,
                                        dx: dirty.x,
                                        dy: dirty.y,
                                        dw: dirty.width,
                                        dh: dirty.height
                                    })
                                );
                            }
                            else {
                                // full image
                                const bmp = image.toBitmap();
                                const offset = append(bmp);
                                conn.send(
                                    JSON.stringify({
                                        type: "fullframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: offset,
                                        byteLength: bmp.byteLength
                                    })
                                );
                            }
                        })
                        break;
                    case "requestfullframe":
                        if (win) {
                            win.webContents.capturePage().then(function (image) {
                                if (!connected) return;
                                const size = image.getSize();
                                const bmp = image.toBitmap();
                                const offset = append(bmp);
                                conn.send(
                                    JSON.stringify({
                                        type: "fullframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: offset,
                                        byteLength: bmp.byteLength
                                    })
                                );
                            }).catch((e) => { console.error(e); });
                        }
                        break;
                    case "opendevtools":
                        if (!win) return;
                        win.webContents.openDevTools({ mode: "detach" });
                        break;
                    case "resize":
                        if(win) win.setContentSize(cmd.width, cmd.height, false);
                        break;
                    case "inputevent":
                        win.webContents.sendInputEvent(cmd.event);
                        break;
                    case "setfocus":
                        if (cmd.focus) win.focus();
                        else win.blur();
                        break;
                    case "custom":
                        const f = new Function("win", "electron", "socket", cmd.js);
                        const res = f.call(win, win, electron, conn);
                        if (cmd.id) {
                            conn.send(JSON.stringify({ type: "result", id: cmd.id, result: res }));
                        }
                        break
                    default:
                        break;
                }
            }

            conn.on("error", function (err) { close(); });
            conn.on("close", function (code, reason) { close(); })
            conn.on("text", function (str) {
                try {
                    const cmd = JSON.parse(str);
                    if (cmd.command) command(cmd);
                    else console.warn("bad command", cmd);
                } catch(err) {
                    console.error("bad command (not JSON)", str, err);
                }
            });
        });

    server.on("error", function (err) {
        console.error(err);
    });
    server.listen(port, "127.0.0.1");
}

function ready() {
  var argv = process.argv;
  if(!argv) argv = [];

  var res = getopt.create(options).bindHelp().parse(argv); 
  var opt = res.options;
  
  if(opt.server) {
      runOffscreenServer(opt.server);
  }
  else {
	var plat = os.platform();

    var name = plat == 'darwin' ? "PRo3D.Viewer" : "PRo3D.Viewer.exe";
    var p = path.join(path.dirname(process.resourcesPath), "build", "build", name)
    console.warn("path = " + p)
    if (fs.existsSync("./build/build/" + name)) {
      console.log('dev')
      p = "./build/build/" + name;
    } else {
      console.log('deployed.')
    }

    var args = process.argv;
    args.shift();
	
	const WINDOW_WIDTH = 640;
    const WINDOW_HEIGHT = 300;
  
    //Definindo centro da tela principal
    let bounds = screen.getPrimaryDisplay().bounds;
    let x = bounds.x + ((bounds.width - WINDOW_WIDTH) / 2);
    let y = bounds.y + ((bounds.height - WINDOW_HEIGHT) / 2);
  

    const splash = new BrowserWindow({ 
        width: WINDOW_WIDTH, 
        height: WINDOW_HEIGHT, 
        center: true,
        x:x,
        y:y,
        frame: false, 
        transparent: true,
        webPreferences : {  
          devTools: false
        } 
      }
    );
    splash.loadURL(`file://${__dirname}/splash.html`)
    splash.show()
    console.log('showed.')
	
    const runningProcess = proc.spawn(p, ["--server"].concat(args));
    console.log('spawned.' + ["--server"].concat(args))
    const rx = /.*url:[ \t]+(.*)/;        

    runningProcess.stdout.on("data", (data) => {
        const m = data.toString().match(rx);
        if (m) { 
				console.log('matched.' + data)
		console.log('matched.' + m)
		console.log('matched.' + m[1])
            createWindow(m[1], () => { console.log('closing.'); splash.close(); });
        }
    });
      
    // Quit when all windows are closed.
    app.on('window-all-closed', function () {
      // On OS X it is common for applications and their menu bar
      // to stay active until the user quits explicitly with Cmd + Q
      if (process.platform !== 'darwin') {
        app.quit()
      }
    })

    app.on('activate', function () {
      // On OS X it's common to re-create a window in the app when the
      // dock icon is clicked and there are no other windows open.
      if (mainWindow === null) {
        createWindow()
      }
    })

    // In this file you can include the rest of your app's specific main process
    // code. You can also put them in separate files and require them here.
  }
  
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on('ready', ready)


