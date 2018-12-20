package com.horine.emailAttachmentDownloader;


import javax.imageio.ImageIO;
import javax.swing.*;
import java.awt.*;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.awt.image.BufferedImage;
import java.io.File;
import java.io.IOException;
import java.text.SimpleDateFormat;
import java.util.Date;

public class DisplayElem extends JPanel{

    int id;
    String date;
    String text;
    boolean onScreen;
    boolean toRemove;
    DisplayPage page;

    public DisplayElem(DisplayPage p, int id, String text){
        super();
        this.page = p;
        this.id = id;
        SimpleDateFormat sdf = new SimpleDateFormat("MM/dd/yy HH:mm");
        Date dt = new Date();
        this.date = sdf.format(dt);
        this.text = text;
        onScreen = false;
        toRemove = false;
        setupFrame();
    }

    public DisplayElem(DisplayPage p, int id, String date, String text){
        super();
        this.page = p;
        this.id = id;
        this.date = date;
        this.text = text;
        onScreen = false;
        toRemove = false;
        setupFrame();
    }
    private void setupFrame(){
        setMaximumSize(new Dimension(400,100));
        setMinimumSize(new Dimension(380,100));

        setLayout(new BorderLayout());
        setPreferredSize(new Dimension(387,100));
        setBorder(BorderFactory.createBevelBorder(0));
        setBackground(Color.ORANGE);
        setToolTipText(text);
        add(setupLeftPane());
        addCloseButton();
    }

    private void close(){
        toRemove = true;
        page.update();
    }

    public void setOnScreen(boolean onScreen) {
        this.onScreen = onScreen;
    }
    public boolean getOnScreen(){
        return onScreen;
    }

    public int getId(){
        return id;
    }

    public void setId(int id){
        this.id = id;
    }

    public String getDate(){
        return this.date;
    }

    public String getText() {
        return text;
    }

    private void addCloseButton(){
        BufferedImage buttonIcon = null;
        try {
            buttonIcon = ImageIO.read(new File("close.png"));
        } catch (IOException e) {
            e.printStackTrace();
        }
        JButton closeButton = new JButton(new ImageIcon(buttonIcon));
        closeButton.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseReleased(MouseEvent e) {close();}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e) {}
        });
        closeButton.setMaximumSize(new Dimension(20,20));
        closeButton.setPreferredSize(new Dimension(20,20));
        closeButton.setMinimumSize(new Dimension(20,20));
        closeButton.setBorder(BorderFactory.createEmptyBorder());
        closeButton.setContentAreaFilled(false);
        JPanel p = new JPanel();
        p.setLayout(new FlowLayout());
        p.setBackground(this.getBackground());
        p.add(closeButton);
        add(p,BorderLayout.EAST);
    }

    private JPanel setupLeftPane(){
        JPanel panel = new JPanel();
        panel.setLayout(new BorderLayout());
        JTextPane datePane = new JTextPane();
        datePane.setText(date);
        datePane.setEditable(false);
        datePane.setBackground(this.getBackground());
        datePane.setMargin(new Insets(5,5,5,5));
        JTextPane textPane = new JTextPane();
        textPane.setText(text);
        textPane.setEditable(false);
        textPane.setBackground(this.getBackground());
        textPane.setMargin(new Insets(5,15,5,5));
        panel.add(datePane, BorderLayout.NORTH);
        panel.add(textPane, BorderLayout.CENTER);

        return panel;
    }
}
