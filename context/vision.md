# Vision

> What JassCardEye is, where it is headed, the guiding ideas behind it and the deliberate non-goals.

## Guiding idea

JassCardEye recognises the **topmost card on a stack of overlapping Jass cards** – live via the
smartphone camera, with low latency. This is the core of the project and what training and
evaluation are geared towards.

After a game, one party's tricks lie as a pile. Laying those cards down one by one under the phone,
with the app recognising the card on top each time, counts the points of the pile without anyone
adding them up.

## Where it stands

JassCardEye is a counting app for iPhone and Android, distributed through the App Store and Google
Play. It is the proof of concept of a CAS thesis and a product at the same time: thesis and app share
one code base and one model.

The two apps are one app on two platforms. Every feature, text and design change lands on iOS and
Android in the same change.

## What the system should do

- From a camera image of a card stack, **classify the topmost card** (one of 72 classes - both
  decks, 36 cards each) and give its position in the image. A round is played with one deck, so the
  app accepts only the 36 cards of the deck chosen for it.
- Robust against realistic capture conditions: camera tilts from straight above to 60°, varying
  distances above the table, different **exposure**, and different **table surfaces / Jass mats**.
- Runs on-device - Core ML on the iPhone, LiteRT (formerly TensorFlow Lite) on Android, both exported
  from the same trained weights - fast enough for use at the game table.

## How we get there

The bottleneck is data acquisition. Instead of photographing and labelling thousands of stacks by
hand, we generate **synthetic training data** from 72 high-resolution real scans - the French-suited
and the German-suited deck, 36 cards each. A rendering pipeline composes scenes (background → 0..35
underlying cards → topmost card, all from one deck), simulates the camera as a pinhole camera - its
tilt swept evenly from 0° to 60° over a run, direction and distance drawn at random - and delivers
the image plus the label of the topmost card. The label is exact and free because the scene is known.

Real photos play a smaller, deliberate part: they are the scans the scenes are built from, some of
the empty-table negatives, and the whole validation set (`data/real/val`) - so a metric says how the
model does on real piles, not how well the renderer matches itself.

The thesis investigates three ways of recognising the top card, trained on the same generated images
and validated on the same real photos: **A** classifies the whole image, **B** locates the card and
classifies the rectified crop, **C** detects and classifies in one pass. After the thesis only **C,
the detector,** is pursued; it is the model both apps ship. A and B stay in the code and remain
trainable, but are not used in the apps.

The project definition also asks how real and synthetic training data compare. That comparison is
not part of the project: the real card data is used to build the synthetic training images rather
than as a training set of its own, so there is no hand-labelled training set to compare against.

## Deliberate non-goals

- **No recognition of every card in the stack** – only the topmost one. Everything else is occluded.
- **No Tafel.** The app counts the card points of a pile after the game - discipline, last trick,
  factor - but does not write Weis or Stöck, keep score across rounds, or track target scores.
- **No standing or tilted cards** in the generated data: cards lie flat, with realistic stack height
  and rounded corners. Empty tables (some of them real photos), soft contact shadows, lamp or diffuse
  light, motion blur and a card passing over the pile are generated.

## Picture of success

A counting app on iPhone and Android that, held over a real card pile, names the topmost card
reliably and quickly - trained on synthetic data, validated on real photos.

In the stores, success looks like this: the app is free and the demo is the whole app – start
questions, recognition, pile, factor – with only the counted points blurred. One purchase of CHF 5,
once, on the App Store or Google Play, makes them readable for good: no subscription, no account, no
server.
