"use strict";
import React, { Component } from "react";
import { View, NativeModules, NativeEventEmitter, Dimensions, StyleSheet } from "react-native";

import HideableView from "./HideableView";
import ContentPicture from "./ContentPicture";
import ContentScroll from "./ContentScroll";
import ResourceLoader from "../ResourceLoader";

const width = Dimensions.get('window').width;
const height = Dimensions.get('window').height;

export default class ContentCatalog extends Component {
    constructor(props) {
        super(props);
        this.state = {
            selectedClipIndex: 0
        };
        this.visible = this.props.visibility;
        this.bigPictureVisible = this.visible;
        this.keysListenningOff = false;
        this.toggleVisibility = this.toggleVisibility.bind(this);
        this.onTVKeyDown = this.onTVKeyDown.bind(this);
        this.onTVKeyUp = this.onTVKeyUp.bind(this);
        this.handleSelectedIndexChange = this.handleSelectedIndexChange.bind(this);
        this.handleBigPicLoadStart = this.handleBigPicLoadStart.bind(this);
        this.handleBigPicLoadEnd = this.handleBigPicLoadEnd.bind(this);
        this.JuvoPlayer = NativeModules.JuvoPlayer;
        this.JuvoEventEmitter = new NativeEventEmitter(this.JuvoPlayer);
    }
    componentWillMount() {
        this.JuvoEventEmitter.addListener("onTVKeyDown", this.onTVKeyDown);
        this.JuvoEventEmitter.addListener("onTVKeyUp", this.onTVKeyUp);
    }
    componentDidUpdate(prevProps, prevState) {
        this.bigPictureVisible = true;
    }
    shouldComponentUpdate(nextProps, nextState) {
        return true;
    }
    toggleVisibility() {
        this.visible = !this.visible;
        this.props.switchView("PlaybackView", !this.visible);
    }
    rerender() {
        this.setState({
            selectedIndex: this.state.selectedIndex
        });
    }
    onTVKeyDown(pressed) {
        //There are two parameters available:
        //pressed.KeyName
        //pressed.KeyCode
        if (this.keysListenningOff) return;
        switch (pressed.KeyName) {
            case "XF86Back":
            case "XF86AudioStop":
            case "Return":
            case "XF86AudioPlay":
            case "XF86PlayBack":
                this.toggleVisibility();
                break;
            case ("Left", "Right"):
                break;
        }
        if (this.bigPictureVisible) {
            //hide big picture during the fast scrolling (long key press)
            this.bigPictureVisible = false;
            this.rerender();
        }
    }
    onTVKeyUp(pressed) {
        if (this.keysListenningOff) return;
        this.bigPictureVisible = true;
        this.rerender();
    }

    handleSelectedIndexChange(index) {
        this.props.onSelectedIndexChange(index);
        this.setState({
            selectedClipIndex: index
        });
    }
    handleBigPicLoadStart() {}
    handleBigPicLoadEnd() {
        this.bigPictureVisible = true;
    }
    render() {
        const index = this.state.selectedClipIndex ? this.state.selectedClipIndex : 0;
        const uri = ResourceLoader.tileNames[index];
        const path = ResourceLoader.tilePathSelect(uri);
        const overlay = ResourceLoader.tilesPath.contentDescriptionBackground;
        const visibility = this.props.visibility ? this.props.visibility : this.visible;
        this.visible = visibility;
        this.keysListenningOff = !visibility;
        const showBigPicture = this.bigPictureVisible;
        return (
            <HideableView visible={visibility} duration={300}>
                <HideableView visible={showBigPicture} duration={100}>
                    <View style={styles.page}>
                        <View style={[styles.row, {flex: 7, flexDirection: 'row',}]}>
                            <View style={[styles.row, {flex: 1}]}/>
                                <View style={[styles.row, {flex: 2}]}>
                                <ContentPicture position={'absolute'} source={uri} selectedIndex={index} path={path}
                                                onLoadEnd={this.handleBigPicLoadEnd} onLoadStart={this.handleBigPicLoadStart}
                                                width={'100%'} height={'100%'}/>
                                                
                                <ContentPicture position={'absolute'} source={uri} selectedIndex={index} path={overlay}
                                                width={'100%'} height={'100%'}/>
                            </View>
                        </View>
                        <View style={[styles.row, {flex: 3}]}/>
                    </View>
                </HideableView>
                <View style={[styles.page, {position: 'absolute'}]}>
                    <ContentScroll
                        onSelectedIndexChange={this.handleSelectedIndexChange}
                        contentURIs={ResourceLoader.tileNames}
                        keysListenningOff={this.keysListenningOff}
                    />
                </View>
            </HideableView>
        );
    }
}

const styles = StyleSheet.create({
    page: {
        width: width,
        height: height,
    },
    row: {
        backgroundColor: 'black',
    }
});