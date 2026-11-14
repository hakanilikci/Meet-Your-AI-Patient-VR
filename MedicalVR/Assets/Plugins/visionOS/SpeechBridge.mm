// On-device speech-to-text for the student's question (Apple Speech framework), so talking works
// without ai's speech-to-text key. Unity calls these through DllImport("__Internal") (SpeechInput.cs);
// the text comes back with UnitySendMessage(<object>, "OnSpeechResult", text).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Speech/Speech.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static SFSpeechRecognizer* recognizer;
static AVAudioEngine* engine;
static SFSpeechAudioBufferRecognitionRequest* request;
static SFSpeechRecognitionTask* task;
static NSString* lastText = @"";
static NSString* target = @"App";

extern "C" void MVR_SpeechAuthorize() {
    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {}];
}

extern "C" bool MVR_SpeechAvailable() {
    if (!recognizer) recognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:@"en-US"]];
    return recognizer != nil && recognizer.isAvailable &&
           [SFSpeechRecognizer authorizationStatus] == SFSpeechRecognizerAuthorizationStatusAuthorized;
}

extern "C" bool MVR_SpeechStart(const char* gameObject) {
    if (!MVR_SpeechAvailable()) return false;
    target = [NSString stringWithUTF8String:gameObject];
    lastText = @"";
    NSError* err = nil;
    AVAudioSession* session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayAndRecord mode:AVAudioSessionModeDefault
                 options:AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionMixWithOthers error:&err];
    [session setActive:YES error:&err];

    engine = [[AVAudioEngine alloc] init];
    request = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
    request.shouldReportPartialResults = YES;
    if (recognizer.supportsOnDeviceRecognition) request.requiresOnDeviceRecognition = YES;   // works without internet

    AVAudioInputNode* input = engine.inputNode;
    AVAudioFormat* format = [input outputFormatForBus:0];
    SFSpeechAudioBufferRecognitionRequest* req = request;
    [input installTapOnBus:0 bufferSize:1024 format:format block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
        [req appendAudioPCMBuffer:buffer];
    }];
    task = [recognizer recognitionTaskWithRequest:request resultHandler:^(SFSpeechRecognitionResult* result, NSError* error) {
        if (result) lastText = result.bestTranscription.formattedString;
    }];
    [engine prepare];
    if (![engine startAndReturnError:&err]) {
        NSLog(@"[MedicalVR] speech: audio engine failed: %@", err);
        return false;
    }
    return true;
}

extern "C" void MVR_SpeechStop() {
    if (engine) { [engine stop]; [engine.inputNode removeTapOnBus:0]; }
    if (request) [request endAudio];
    // give the recognizer a moment for the final words, then hand the text to Unity
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.9 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (task) [task finish];
        task = nil; request = nil; engine = nil;
        UnitySendMessage([target UTF8String], "OnSpeechResult", [lastText UTF8String] ?: "");
    });
}
