require('dotenv').config();
const { notarize } = require('electron-notarize');


exports.default = async function(context) {

  const { electronPlatformName, appOutDir } = context;
  if (electronPlatformName !== 'darwin') {
    return;
  }
 
  const appName = context.packager.appInfo.productFilename;

  return await notarize({
    appBundleId: 'com.aardvarkians.aardium',
    appPath: `${appOutDir}/${appName}.app`,
    appleId: "gh@aardworx.at",
    appleIdPassword: process.env.MAC_DEV_PASSWORD,
    ascProvider: "4LQPQ4H9LQ"
  });


}   