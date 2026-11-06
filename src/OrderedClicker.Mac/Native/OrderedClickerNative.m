#import <Cocoa/Cocoa.h>
#import <Carbon/Carbon.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <stdatomic.h>
#import <unistd.h>
#import <pthread.h>
#import <stdint.h>
#import <math.h>

// C ABI only: no Objective-C object ownership crosses the managed boundary.
static atomic_uint pendingEvents;
static EventHotKeyRef hotkeys[4];
static EventHandlerRef keyHandler;
static BOOL initialized;
typedef struct {
    uint64_t sequence;
    int32_t id, x, y, positionValid;
} OCHotKeyEvent;
static OCHotKeyEvent keyEvents[256];
static size_t keyHead, keyCount;
static uint64_t keySequence;
static pthread_mutex_t keyLock = PTHREAD_MUTEX_INITIALIZER;
static OSStatus HandleKey(EventHandlerCallRef next, EventRef event, void *data) {
    EventHotKeyID key;
    if (GetEventParameter(event, kEventParamDirectObject, typeEventHotKeyID, NULL,
                          sizeof(key), NULL, &key) == noErr && key.id >= 1 && key.id <= 3) {
        CGEventRef cursor = CGEventCreate(NULL);
        CGPoint p = cursor ? CGEventGetLocation(cursor) : CGPointZero;
        OCHotKeyEvent value = { 0, (int32_t)key.id, (int32_t)llround(p.x),
            (int32_t)llround(p.y), cursor != NULL };
        if (cursor) CFRelease(cursor);
        pthread_mutex_lock(&keyLock);
        value.sequence = ++keySequence;
        if (keyCount == 256) {
            atomic_fetch_or(&pendingEvents, 32u);
        } else {
            keyEvents[(keyHead + keyCount) % 256] = value;
            keyCount++;
        }
        pthread_mutex_unlock(&keyLock);
    }
    return noErr;
}
static void DisplayChanged(CGDirectDisplayID display, CGDisplayChangeSummaryFlags flags, void *data) {
    if (!(flags & kCGDisplayBeginConfigurationFlag)) atomic_fetch_or(&pendingEvents, 16u);
}
int oc_initialize(void) {
    if (initialized) return 1;
    initialized = YES;
    EventTypeSpec type = { kEventClassKeyboard, kEventHotKeyPressed };
    OSStatus status = InstallEventHandler(GetApplicationEventTarget(), HandleKey, 1, &type, NULL, &keyHandler);
    CGDisplayRegisterReconfigurationCallback(DisplayChanged, NULL);
    NSNotificationCenter *nc = NSWorkspace.sharedWorkspace.notificationCenter;
    for (NSString *name in @[NSWorkspaceWillSleepNotification, NSWorkspaceSessionDidResignActiveNotification])
        [nc addObserverForName:name object:nil queue:nil usingBlock:^(NSNotification *n) {
            atomic_fetch_or(&pendingEvents, 8u);
        }];
    [NSDistributedNotificationCenter.defaultCenter addObserverForName:@"com.apple.screenIsLocked"
        object:nil queue:nil usingBlock:^(NSNotification *n) { atomic_fetch_or(&pendingEvents, 8u); }];
    return status == noErr;
}
unsigned int oc_poll_events(void) { return atomic_exchange(&pendingEvents, 0); }
int oc_dequeue_hotkey(OCHotKeyEvent *value) {
    if (!value) return 0;
    pthread_mutex_lock(&keyLock);
    if (!keyCount) { pthread_mutex_unlock(&keyLock); return 0; }
    *value = keyEvents[keyHead]; keyHead = (keyHead + 1) % 256; keyCount--;
    pthread_mutex_unlock(&keyLock);
    return 1;
}
int oc_accessibility(int prompt) {
    NSDictionary *options = @{(__bridge NSString *)kAXTrustedCheckOptionPrompt: @(prompt != 0)};
    return AXIsProcessTrustedWithOptions((__bridge CFDictionaryRef)options) ? 1 : 0;
}
int oc_screen_permission(int prompt) {
    return (prompt ? CGRequestScreenCaptureAccess() : CGPreflightScreenCaptureAccess()) ? 1 : 0;
}
int oc_cursor(int *x, int *y) {
    CGEventRef e = CGEventCreate(NULL);
    if (!e) return 0;
    CGPoint p = CGEventGetLocation(e);
    CFRelease(e); *x = (int)llround(p.x); *y = (int)llround(p.y); return 1;
}
int oc_move(int x, int y) {
    if (!oc_accessibility(0) || !CGPreflightPostEventAccess()) return 0;
    CGEventRef e = CGEventCreateMouseEvent(NULL, kCGEventMouseMoved, CGPointMake(x,y), kCGMouseButtonLeft);
    if (!e) return 0;
    CGEventPost(kCGHIDEventTap, e); CFRelease(e); return 1;
}
static NSDictionary *TopWindowAtPoint(CGPoint point) {
    CFArrayRef list = CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID);
    if (!list) return nil;
    NSArray *windows = CFBridgingRelease(list);
    for (NSDictionary *window in windows) {
        CGRect bounds;
        if (![window[(__bridge NSString *)kCGWindowAlpha] doubleValue]) continue;
        if (!CGRectMakeWithDictionaryRepresentation(
            (__bridge CFDictionaryRef)window[(__bridge NSString *)kCGWindowBounds], &bounds)) return nil;
        if (CGRectContainsPoint(bounds, point)) return window;
    }
    return nil;
}
int oc_own_ui_at(int x, int y) {
    @autoreleasepool {
        NSRunningApplication *front = NSWorkspace.sharedWorkspace.frontmostApplication;
        NSDictionary *window = TopWindowAtPoint(CGPointMake(x,y));
        NSNumber *owner = window[(__bridge NSString *)kCGWindowOwnerPID];
        if (!front || !owner) return -1;
        return front.processIdentifier == getpid() || owner.intValue == getpid() ? 1 : 0;
    }
}
static BOOL CopyUTF8(NSString *value, char *output, int capacity) {
    if (!value.length || !output || capacity <= 0) return NO;
    NSData *data = [value dataUsingEncoding:NSUTF8StringEncoding];
    if (!data || data.length >= (NSUInteger)capacity) return NO;
    memcpy(output, data.bytes, data.length); output[data.length] = 0; return YES;
}
int oc_probe_window(int x, int y, char *application, int applicationCapacity,
    char *title, int titleCapacity, int *foregroundPid, int *windowPid, int *ownProcess) {
    @autoreleasepool {
        if (!oc_accessibility(0)) return 0;
        NSRunningApplication *front = NSWorkspace.sharedWorkspace.frontmostApplication;
        NSDictionary *hit = TopWindowAtPoint(CGPointMake(x,y));
        NSNumber *owner = hit[(__bridge NSString *)kCGWindowOwnerPID];
        if (!front || !owner || owner.intValue <= 0) return 0;
        *foregroundPid = front.processIdentifier; *windowPid = owner.intValue;
        *ownProcess = front.processIdentifier == getpid() || owner.intValue == getpid();
        NSRunningApplication *app = [NSRunningApplication runningApplicationWithProcessIdentifier:owner.intValue];
        if (!app.bundleIdentifier.length) return 0;

        AXUIElementRef system = AXUIElementCreateSystemWide(), element = NULL;
        if (!system) return 0;
        AXUIElementSetMessagingTimeout(system, 0.3);
        AXError status = AXUIElementCopyElementAtPosition(system, x, y, &element);
        CFRelease(system);
        if (status != kAXErrorSuccess || !element) return 0;
        pid_t actualPid = 0;
        if (AXUIElementGetPid(element, &actualPid) != kAXErrorSuccess || actualPid != owner.intValue) {
            CFRelease(element); return 0;
        }
        CFTypeRef window = NULL;
        CFTypeRef role = NULL;
        AXUIElementCopyAttributeValue(element, kAXRoleAttribute, &role);
        if (role && CFEqual(role, kAXWindowRole)) window = CFRetain(element);
        else AXUIElementCopyAttributeValue(element, kAXWindowAttribute, &window);
        if (role) CFRelease(role);
        CFRelease(element);
        if (!window || CFGetTypeID(window) != AXUIElementGetTypeID()) {
            if (window) CFRelease(window); return 0;
        }
        CFTypeRef position = NULL, size = NULL, name = NULL;
        AXUIElementCopyAttributeValue((AXUIElementRef)window, kAXPositionAttribute, &position);
        AXUIElementCopyAttributeValue((AXUIElementRef)window, kAXSizeAttribute, &size);
        AXUIElementCopyAttributeValue((AXUIElementRef)window, kAXTitleAttribute, &name);
        CGPoint origin; CGSize dimensions; CGRect cgBounds;
        BOOL valid = position && size && name &&
            CFGetTypeID(position) == AXValueGetTypeID() &&
            CFGetTypeID(size) == AXValueGetTypeID() &&
            CFGetTypeID(name) == CFStringGetTypeID() &&
            AXValueGetValue((AXValueRef)position, kAXValueCGPointType, &origin) &&
            AXValueGetValue((AXValueRef)size, kAXValueCGSizeType, &dimensions) &&
            CGRectMakeWithDictionaryRepresentation(
                (__bridge CFDictionaryRef)hit[(__bridge NSString *)kCGWindowBounds], &cgBounds);
        // AX describes the actual hit element's window; CG confirms that window is on top.
        if (valid) valid = fabs(origin.x-cgBounds.origin.x) <= 4 && fabs(origin.y-cgBounds.origin.y) <= 4 &&
            fabs(dimensions.width-cgBounds.size.width) <= 4 && fabs(dimensions.height-cgBounds.size.height) <= 4;
        NSString *cgTitle = hit[(__bridge NSString *)kCGWindowName];
        if (valid && cgTitle.length) valid = [cgTitle isEqualToString:(__bridge NSString *)name];
        if (valid) valid = CopyUTF8(app.bundleIdentifier,application,applicationCapacity) &&
            CopyUTF8((__bridge NSString *)name,title,titleCapacity);
        if (position) CFRelease(position);
        if (size) CFRelease(size);
        if (name) CFRelease(name);
        CFRelease(window);
        return valid ? 1 : 0;
    }
}
int oc_click_at(int x, int y, int releaseOnly) {
    if (!oc_accessibility(0) || !CGPreflightPostEventAccess()) return 0;
    if (!releaseOnly && oc_own_ui_at(x,y) != 0) return 0;
    CGPoint p = CGPointMake(x,y);
    CGEventRef up = CGEventCreateMouseEvent(NULL, kCGEventLeftMouseUp, p, kCGMouseButtonLeft);
    if (!up) return 0;
    if (!releaseOnly) {
        CGEventRef down = CGEventCreateMouseEvent(NULL, kCGEventLeftMouseDown, p, kCGMouseButtonLeft);
        if (!down) { CFRelease(up); return 0; }
        CGEventSetIntegerValueField(down, kCGMouseEventClickState, 1);
        CGEventPost(kCGHIDEventTap, down); CFRelease(down);
    }
    CGEventSetIntegerValueField(up, kCGMouseEventClickState, 1);
    CGEventPost(kCGHIDEventTap, up); CFRelease(up); return 1;
}
// id, x, y, width, height, backing-scale*1000; all positions are CG global points.
int oc_displays(double *output, int capacity) {
    CGDirectDisplayID ids[32]; uint32_t count = 0;
    if (CGGetActiveDisplayList(32, ids, &count) != kCGErrorSuccess) return -1;
    int n = MIN((int)count, capacity);
    for (int i = 0; i < n; i++) {
        CGRect b = CGDisplayBounds(ids[i]);
        CGDisplayModeRef mode = CGDisplayCopyDisplayMode(ids[i]);
        double scale = mode && CGDisplayModeGetWidth(mode) ?
            (double)CGDisplayModeGetPixelWidth(mode) / CGDisplayModeGetWidth(mode) : 1;
        if (mode) CGDisplayModeRelease(mode);
        output[i*6] = ids[i]; output[i*6+1] = b.origin.x; output[i*6+2] = b.origin.y;
        output[i*6+3] = b.size.width; output[i*6+4] = b.size.height; output[i*6+5] = scale;
    }
    return n;
}
int oc_register_hotkey(int id, unsigned int key, unsigned int modifiers) {
    if (id < 1 || id > 3 || !keyHandler) return -1;
    if (hotkeys[id]) return -2;
    EventHotKeyID ident = { 'OClk', (UInt32)id };
    return RegisterEventHotKey(key, modifiers, ident, GetApplicationEventTarget(), 0, &hotkeys[id]);
}
int oc_unregister_hotkey(int id) {
    if (id < 1 || id > 3 || !hotkeys[id]) return 0;
    OSStatus status = UnregisterEventHotKey(hotkeys[id]);
    if (!status) hotkeys[id] = NULL;
    return status;
}
void oc_activate_existing(void) {
    NSString *ident = NSBundle.mainBundle.bundleIdentifier;
    for (NSRunningApplication *app in [NSRunningApplication runningApplicationsWithBundleIdentifier:ident]) {
        if (app.processIdentifier != getpid()) [app activateWithOptions:NSApplicationActivateIgnoringOtherApps];
    }
}

@interface OCFrame : NSObject <SCStreamOutput, SCStreamDelegate>
@property(nonatomic,strong) SCStream *stream;
@property(nonatomic) CGRect bounds;
@property(nonatomic,strong) NSData *pixels;
@property(nonatomic) size_t width;
@property(nonatomic) size_t height;
@property(nonatomic) size_t stride;
@property(nonatomic) BOOL failed;
@end
@implementation OCFrame
- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error {
    @synchronized(self) { self.failed = YES; }
}
- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)buffer ofType:(SCStreamOutputType)type {
    if (type != SCStreamOutputTypeScreen || !CMSampleBufferIsValid(buffer)) return;
    CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(buffer, false);
    if (!attachments || CFArrayGetCount(attachments) == 0) return;
    NSDictionary *meta = (__bridge NSDictionary *)CFArrayGetValueAtIndex(attachments, 0);
    if ([meta[SCStreamFrameInfoStatus] integerValue] != SCFrameStatusComplete) return;
    CVPixelBufferRef image = CMSampleBufferGetImageBuffer(buffer);
    if (!image) return;
    CVPixelBufferLockBaseAddress(image, kCVPixelBufferLock_ReadOnly);
    @synchronized(self) {
        self.width = CVPixelBufferGetWidth(image); self.height = CVPixelBufferGetHeight(image);
        self.stride = CVPixelBufferGetBytesPerRow(image);
        self.pixels = [NSData dataWithBytes:CVPixelBufferGetBaseAddress(image) length:self.stride*self.height];
    }
    CVPixelBufferUnlockBaseAddress(image, kCVPixelBufferLock_ReadOnly);
}
@end
@interface OCSampler : NSObject
@property(nonatomic,strong) NSMutableArray<OCFrame *> *frames;
@property(nonatomic) CGRect region;
@end
@implementation OCSampler
@end

void *oc_sampler_create(int x, int y, int w, int h) {
    @autoreleasepool {
        if (!CGPreflightScreenCaptureAccess() || w <= 0 || h <= 0) return NULL;
        // This function must be called off the UI thread: ScreenCaptureKit completes asynchronously.
        dispatch_semaphore_t ready = dispatch_semaphore_create(0);
        __block SCShareableContent *content = nil;
        [SCShareableContent getShareableContentExcludingDesktopWindows:NO onScreenWindowsOnly:YES
            completionHandler:^(SCShareableContent *value, NSError *error) {
                content = value; dispatch_semaphore_signal(ready);
            }];
        if (dispatch_semaphore_wait(ready, dispatch_time(DISPATCH_TIME_NOW, 8*NSEC_PER_SEC)) || !content) return NULL;
        OCSampler *sampler = [OCSampler new]; sampler.frames = [NSMutableArray new];
        sampler.region = CGRectMake(x,y,w,h);
        NSArray *ownApps = [content.applications filteredArrayUsingPredicate:
            [NSPredicate predicateWithBlock:^BOOL(SCRunningApplication *app, NSDictionary *bindings) {
                return app.processID == getpid();
            }]];
        for (SCDisplay *display in content.displays) {
            CGRect bounds = CGDisplayBounds(display.displayID);
            if (CGRectIsNull(CGRectIntersection(bounds, sampler.region))) continue;
            OCFrame *frame = [OCFrame new]; frame.bounds = bounds;
            SCContentFilter *filter = [[SCContentFilter alloc] initWithDisplay:display
                excludingApplications:ownApps exceptingWindows:@[]];
            SCStreamConfiguration *config = [SCStreamConfiguration new];
            config.width = (size_t)bounds.size.width; config.height = (size_t)bounds.size.height;
            config.pixelFormat = kCVPixelFormatType_32BGRA;
            config.minimumFrameInterval = CMTimeMake(1, 10); config.queueDepth = 3; config.showsCursor = NO;
            frame.stream = [[SCStream alloc] initWithFilter:filter configuration:config delegate:frame];
            NSError *error = nil;
            BOOL added = [frame.stream addStreamOutput:frame type:SCStreamOutputTypeScreen
                sampleHandlerQueue:dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0) error:&error];
            if (!added || error) { frame.failed = YES; }
            [sampler.frames addObject:frame];
            [frame.stream startCaptureWithCompletionHandler:^(NSError *e) {
                if (e) { @synchronized(frame) { frame.failed = YES; } }
            }];
        }
        if (!sampler.frames.count) return NULL;
        return (__bridge_retained void *)sampler;
    }
}
// Returns 1 for a valid frame, 0 while warming up, -1 for permission/stream failure.
int oc_sampler_read(void *handle, unsigned char *output, int width, int height) {
    @autoreleasepool {
        OCSampler *sampler = (__bridge OCSampler *)handle;
        if (!sampler || !CGPreflightScreenCaptureAccess()) return -1;
        memset(output,0,width*height*3);
        for (OCFrame *frame in sampler.frames) {
            @synchronized(frame) {
                if (frame.failed) return -1;
                if (!frame.pixels) return 0;
                const unsigned char *data = frame.pixels.bytes;
                for (int oy = 0; oy < height; oy++) for (int ox = 0; ox < width; ox++) {
                    double gx = sampler.region.origin.x + (ox+0.5)*sampler.region.size.width/width;
                    double gy = sampler.region.origin.y + (oy+0.5)*sampler.region.size.height/height;
                    if (!CGRectContainsPoint(frame.bounds,CGPointMake(gx,gy))) continue;
                    size_t sx = MIN(frame.width-1,(size_t)((gx-frame.bounds.origin.x)*frame.width/frame.bounds.size.width));
                    size_t sy = MIN(frame.height-1,(size_t)((gy-frame.bounds.origin.y)*frame.height/frame.bounds.size.height));
                    const unsigned char *pixel = data+sy*frame.stride+sx*4;
                    size_t offset = ((size_t)oy*width+ox)*3;
                    output[offset] = pixel[2]; output[offset+1] = pixel[1]; output[offset+2] = pixel[0];
                }
            }
        }
        return 1;
    }
}
void oc_sampler_destroy(void *handle) {
    @autoreleasepool {
        if (!handle) return;
        OCSampler *sampler = (__bridge_transfer OCSampler *)handle;
        for (OCFrame *frame in sampler.frames) {
            SCStream *stream = frame.stream;
            [stream removeStreamOutput:frame type:SCStreamOutputTypeScreen error:nil];
            [stream stopCaptureWithCompletionHandler:^(NSError *error) {}];
            frame.stream = nil;
        }
    }
}
void oc_preview_region(int x,int y,int width,int height) {
    CGFloat primaryHeight = CGDisplayBounds(CGMainDisplayID()).size.height;
    NSRect rect=NSMakeRect(x,primaryHeight-y-height,width,height);
    NSPanel *panel=[[NSPanel alloc] initWithContentRect:rect styleMask:NSWindowStyleMaskBorderless
        backing:NSBackingStoreBuffered defer:NO];
    panel.opaque=NO;panel.backgroundColor=[NSColor colorWithCalibratedRed:0.2 green:0.6 blue:1 alpha:0.16];
    panel.ignoresMouseEvents=YES;panel.level=NSFloatingWindowLevel;
    panel.collectionBehavior=NSWindowCollectionBehaviorCanJoinAllSpaces|NSWindowCollectionBehaviorFullScreenAuxiliary;
    panel.contentView.wantsLayer=YES;panel.contentView.layer.borderWidth=3;
    panel.contentView.layer.borderColor=NSColor.systemBlueColor.CGColor;
    [panel orderFrontRegardless];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW,2*NSEC_PER_SEC),dispatch_get_main_queue(),^{[panel orderOut:nil];});
}
