find "build/build" | while read fname; do
        if [[ -f $fname ]]; then
                echo "[INFO] Signing $fname"
                codesign --force --timestamp --options=runtime --entitlements entitlements.mac.plist --sign "$MAC_IDENTITY" $fname
        fi
done